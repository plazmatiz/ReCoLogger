using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ReCoLogger;

/// <summary>A destination for log records (console, file, UI list, ...).</summary>
public interface ILogSink : IDisposable
{
    void Write(LogEntry entry);

    /// <summary>Forces buffered records to be written out. Optional.</summary>
    void Flush() { }
}

/// <summary>Writes colored output to the console, color depends on the level.</summary>
public sealed class ConsoleSink : ILogSink
{
    static readonly object Gate = new();

    public void Write(LogEntry e)
    {
        lock (Gate)
        {
            try
            {
                Console.ForegroundColor = e.Level switch
                {
                    LogLevel.Trace => ConsoleColor.DarkGray,
                    LogLevel.Debug => ConsoleColor.Gray,
                    LogLevel.Information => ConsoleColor.White,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    _ => ConsoleColor.Magenta,
                };
                Console.WriteLine(e.Format());
            }
            finally
            {
                try { Console.ResetColor(); } catch { /* console may be unavailable */ }
            }
        }
    }

    public void Dispose() { }
}

/// <summary>Writes to the IDE Output window (Debug.WriteLine).</summary>
public sealed class DebugSink : ILogSink
{
    public void Write(LogEntry e) => System.Diagnostics.Debug.WriteLine(e.Format());

    public void Dispose() { }
}

/// <summary>
/// Writes to Android logcat. The library targets plain net8.0, so Android.Util.Log is bound by reflection
/// at runtime; on any other platform this sink does nothing.
/// </summary>
public sealed class LogcatSink : ILogSink
{
    // logcat truncates long messages (about 4000 bytes), so long records are split into chunks.
    const int MaxChunk = 3500;

    // Indexes: 0 Verbose, 1 Debug, 2 Info, 3 Warn, 4 Error.
    static readonly Func<string, string, int>?[] Writers = BindWriters();

    readonly string _tag;

    /// <param name="tag">Logcat tag. Truncated to 23 characters (the limit on older Android versions).</param>
    public LogcatSink(string tag) => _tag = tag.Length > 23 ? tag[..23] : tag;

    public void Write(LogEntry e)
    {
        var writer = Writers[e.Level switch
        {
            LogLevel.Trace => 0,
            LogLevel.Debug => 1,
            LogLevel.Information => 2,
            LogLevel.Warning => 3,
            _ => 4, // Error and Critical. Log.Wtf is avoided on purpose: it can kill the process on some devices.
        }];
        if (writer == null) return;

        var text = $"{e.Source} | {e.Message}" + (e.Exception != null ? "\n" + e.Exception : "");
        for (int i = 0; i < text.Length; i += MaxChunk)
            writer(_tag, text.Substring(i, Math.Min(MaxChunk, text.Length - i)));
    }

    public void Dispose() { }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, "Android.Util.Log", "Mono.Android")]
    static Func<string, string, int>?[] BindWriters()
    {
        var result = new Func<string, string, int>?[5];
        if (!OperatingSystem.IsAndroid()) return result;

        try
        {
            var type = Type.GetType("Android.Util.Log, Mono.Android");
            if (type == null) return result;

            string[] names = ["Verbose", "Debug", "Info", "Warn", "Error"];
            for (int i = 0; i < names.Length; i++)
            {
                var method = type.GetMethod(names[i], new[] { typeof(string), typeof(string) });
                if (method != null)
                    result[i] = (Func<string, string, int>)Delegate.CreateDelegate(typeof(Func<string, string, int>), method);
            }
        }
        catch
        {
            // Binding failed: logcat output is simply unavailable.
        }
        return result;
    }
}

/// <summary>
/// Writes one file per session: log_yyyy-MM-dd_HH-mm-ss.txt. Writing happens on a background
/// task so callers are never blocked. On startup only <c>keepSessions - 1</c> old files are kept,
/// plus the new one.
/// </summary>
public sealed class FileSink : ILogSink
{
    readonly record struct Msg(string? Line, TaskCompletionSource? Flush);

    readonly Channel<Msg> _ch = Channel.CreateUnbounded<Msg>(new UnboundedChannelOptions { SingleReader = true });
    readonly StreamWriter _writer;
    readonly Task _task;

    public string LogDirectory { get; }

    /// <summary>Full path of the file of the current session.</summary>
    public string CurrentFile { get; }

    public FileSink(string directory, int keepSessions = 3)
    {
        LogDirectory = directory;
        Directory.CreateDirectory(directory);
        PruneOld(directory, Math.Max(keepSessions - 1, 0));

        // Two sessions started within the same second must not overwrite each other.
        var baseName = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var path = Path.Combine(directory, baseName + ".txt");
        for (int i = 1; File.Exists(path); i++)
            path = Path.Combine(directory, $"{baseName}_{i}.txt");
        CurrentFile = path;

        _writer = new StreamWriter(
            new FileStream(CurrentFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
            new UTF8Encoding(false));

        _task = Task.Run(RunAsync);
    }

    /// <summary>Deletes the oldest session files so that at most <paramref name="keep"/> remain.</summary>
    public static void PruneOld(string directory, int keep)
    {
        // File names contain a sortable timestamp, so name order == chronological order.
        var files = Directory.GetFiles(directory, "log_*.txt").OrderBy(f => f, StringComparer.Ordinal).ToList();
        for (int i = 0; i < files.Count - keep; i++)
        {
            try { File.Delete(files[i]); } catch { /* the file may be locked by another process */ }
        }
    }

    public void Write(LogEntry e) => _ch.Writer.TryWrite(new Msg(e.Format(), null));

    public void Flush()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_ch.Writer.TryWrite(new Msg(null, tcs)))
            tcs.Task.Wait(TimeSpan.FromSeconds(2));
    }

    async Task RunAsync()
    {
        await foreach (var m in _ch.Reader.ReadAllAsync())
        {
            try
            {
                if (m.Line != null) _writer.WriteLine(m.Line);
                // Flush when asked to, or when the queue has run dry (keeps the file fresh without a flush per line).
                if (m.Flush != null || !_ch.Reader.TryPeek(out _)) _writer.Flush();
            }
            catch { /* the logger must never crash the app */ }
            m.Flush?.TrySetResult();
        }
        try { _writer.Flush(); } catch { }
    }

    public void Dispose()
    {
        _ch.Writer.TryComplete();
        _task.Wait(TimeSpan.FromSeconds(2));
        _writer.Dispose();
    }
}
