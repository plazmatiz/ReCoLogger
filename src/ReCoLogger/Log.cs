using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ReCoLogger;

/// <summary>Logger configuration. Pass it to <c>Log.Init</c> or configure it via <c>AddReCoLogger</c>.</summary>
public sealed class LogOptions
{
    public string AppName { get; set; } = "App";

    /// <summary>Log folder. Default: LocalApplicationData/AppName/logs.</summary>
    public string? Directory { get; set; }

    /// <summary>How many session files to keep (the current one included).</summary>
    public int KeepSessions { get; set; } = 3;

    /// <summary>Records below this level are dropped by the logger itself.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    public bool ToConsole { get; set; } = true;
    public bool ToDebugOutput { get; set; } = true;
    public bool ToLogcat { get; set; } = true;
    public bool ToFile { get; set; } = true;

    /// <summary>Logcat tag. Defaults to AppName.</summary>
    public string? LogcatTag { get; set; }

    /// <summary>How many records the in-memory list keeps.</summary>
    public int StoreCapacity { get; set; } = 5000;

    /// <summary>
    /// Marshals live-list updates to the UI thread, e.g. <c>a => Dispatcher.UIThread.Post(a)</c>.
    /// Required if <see cref="Log.Store"/> is bound to UI.
    /// </summary>
    public Action<Action>? UiDispatch { get; set; }

    /// <summary>Log unhandled exceptions automatically and flush the file before the process dies.</summary>
    public bool CaptureUnhandledExceptions { get; set; } = true;

    /// <summary>
    /// For ILogger records, find the calling method and line by walking the stack.
    /// Costs a few microseconds per record; turn it off if you log very heavily.
    /// Line numbers need PDB files; without them only Class.Method is shown.
    /// </summary>
    public bool CaptureCallerInfo { get; set; } = true;
}

/// <summary>
/// Static facade, usable without DI: Log.Information("..."). The class is taken from the file name,
/// the method and line are filled in automatically. ILogger records (via DI) end up in the same sinks.
/// </summary>
public static class Log
{
    static readonly object Gate = new();
    static readonly AsyncLocal<string?> CurrentScope = new();
    static volatile bool _initialized;
    static volatile bool _closed;
    static ILogSink[] _sinks = [];
    static FileSink? _file;
    static bool _hooksSet;

    public static LogLevel MinLevel { get; set; } = LogLevel.Trace;

    /// <summary>Whether ILogger records should capture the caller method and line.</summary>
    public static bool CaptureCallerInfo { get; set; } = true;

    /// <summary>In-memory records with an observable list (for realtime in-app logs).</summary>
    public static LogStore Store { get; } = new();

    public static string? LogDirectory => _file?.LogDirectory;
    public static string? CurrentLogFile => _file?.CurrentFile;

    /// <summary>
    /// Initializes sinks. Only the first call has any effect (until <see cref="Shutdown"/>), so call it as early
    /// as possible. The first log call before Init also initializes the logger with default options.
    /// </summary>
    public static void Init(LogOptions? options = null)
    {
        lock (Gate)
        {
            if (_initialized) return;
            _closed = false;
            options ??= new LogOptions();
            MinLevel = options.MinLevel;
            CaptureCallerInfo = options.CaptureCallerInfo;

            Store.Capacity = options.StoreCapacity;
            Store.Dispatch = options.UiDispatch;

            var list = new List<ILogSink> { Store };
            if (options.ToConsole) list.Add(new ConsoleSink());
            if (options.ToDebugOutput) list.Add(new DebugSink());
            if (options.ToLogcat) list.Add(new LogcatSink(options.LogcatTag ?? options.AppName));
            if (options.ToFile)
            {
                var dir = options.Directory ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), options.AppName, "logs");
                try
                {
                    _file = new FileSink(dir, options.KeepSessions);
                    list.Add(_file);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ReCoLogger] Could not create the log file: {ex.Message}");
                }
            }
            _sinks = list.ToArray();
            _initialized = true;

            if (options.CaptureUnhandledExceptions && !_hooksSet)
            {
                _hooksSet = true;
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    Write(LogLevel.Critical, "Unhandled exception" + (e.IsTerminating ? " (terminating)" : ""),
                        e.ExceptionObject as Exception, "AppDomain", "UnhandledException", "", 0);
                    Flush();
                };
                TaskScheduler.UnobservedTaskException += (_, e) =>
                    Write(LogLevel.Error, "Unobserved task exception", e.Exception, "TaskScheduler", "UnobservedTaskException", "", 0);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            }
        }

        Write(LogLevel.Information,
            $"=== Session started | {options?.AppName ?? "App"} | .NET {Environment.Version} | {RuntimeInformation.OSDescription} ===",
            null, "Log", "Init", "", 0);
    }

    /// <summary>Adds a custom sink (e.g. one that uploads logs to a server).</summary>
    public static void AddSink(ILogSink sink)
    {
        lock (Gate) _sinks = [.. _sinks, sink];
    }

    /// <summary>Forces all sinks to write out buffered records.</summary>
    public static void Flush()
    {
        foreach (var s in _sinks) { try { s.Flush(); } catch { } }
    }

    /// <summary>Flushes and disposes all sinks. Later log calls are ignored until <see cref="Init"/> is called again.</summary>
    public static void Shutdown()
    {
        ILogSink[] sinks;
        lock (Gate)
        {
            sinks = _sinks;
            _sinks = [];
            _initialized = false;
            _closed = true;
        }
        foreach (var s in sinks) { try { s.Flush(); s.Dispose(); } catch { } }
        _file = null;
    }

    // ---------- Main API ----------

    public static void Trace(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Trace, message, null, null, member, file, line);

    public static void Debug(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Debug, message, null, null, member, file, line);

    public static void Information(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Information, message, null, null, member, file, line);

    public static void Warning(string message, Exception? ex = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Warning, message, ex, null, member, file, line);

    public static void Error(string message, Exception? ex = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Error, message, ex, null, member, file, line);

    public static void Critical(string message, Exception? ex = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Critical, message, ex, null, member, file, line);

    /// <summary>Logger with an exact class name: <c>static readonly ClassLogger _log = Log.For&lt;MyClass&gt;();</c></summary>
    public static ClassLogger For<T>() => new(typeof(T).Name);

    public static ClassLogger For(string className) => new(className);

    /// <summary>
    /// <c>using (Log.BeginScope("Order 42")) { ... }</c> adds context to every log inside the block.
    /// Works across await. ILogger scopes are routed here as well.
    /// </summary>
    public static IDisposable BeginScope(string name)
    {
        var prev = CurrentScope.Value;
        CurrentScope.Value = prev == null ? name : prev + " > " + name;
        return new ScopeToken(prev);
    }

    /// <summary><c>using (Log.Measure("LoadData")) { ... }</c> logs "LoadData took 123 ms" when the block ends.</summary>
    public static IDisposable Measure(string name, LogLevel level = LogLevel.Debug,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => new MeasureToken(name, level, member, file, line);

    // ---------- Core ----------

    /// <summary>Builds a record and sends it to every sink. Used by the facade and by the ILogger provider.</summary>
    internal static void Write(LogLevel level, string message, Exception? ex, string? cls, string member, string file, int line,
        EventId eventId = default, string? category = null)
    {
        if (level == LogLevel.None || level < MinLevel || _closed) return;
        if (!_initialized) Init();

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            ClassName = cls ?? ClassFromPath(file),
            MethodName = member,
            FilePath = file,
            Line = line,
            ThreadId = Environment.CurrentManagedThreadId,
            Exception = ex,
            Scope = CurrentScope.Value,
            EventId = eventId,
            Category = category,
        };

        foreach (var s in _sinks)
        {
            try { s.Write(entry); } catch { /* the logger must never crash the app */ }
        }
    }

    static string ClassFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        int i = path.LastIndexOfAny(['/', '\\']);
        var name = i >= 0 ? path[(i + 1)..] : path;
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    sealed class ScopeToken(string? prev) : IDisposable
    {
        public void Dispose() => CurrentScope.Value = prev;
    }

    sealed class MeasureToken(string name, LogLevel level, string member, string file, int line) : IDisposable
    {
        readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose() =>
            Write(level, $"{name} took {Stopwatch.GetElapsedTime(_start).TotalMilliseconds:0.#} ms", null, null, member, file, line);
    }
}

/// <summary>Logger bound to a specific class name (used instead of the file name).</summary>
public sealed class ClassLogger
{
    readonly string _cls;

    internal ClassLogger(string cls) => _cls = cls;

    public void Trace(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log.Write(LogLevel.Trace, message, null, _cls, member, file, line);

    public void Debug(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log.Write(LogLevel.Debug, message, null, _cls, member, file, line);

    public void Information(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log.Write(LogLevel.Information, message, null, _cls, member, file, line);

    public void Warning(string message, Exception? ex = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log.Write(LogLevel.Warning, message, ex, _cls, member, file, line);

    public void Error(string message, Exception? ex = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log.Write(LogLevel.Error, message, ex, _cls, member, file, line);

    public void Critical(string message, Exception? ex = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log.Write(LogLevel.Critical, message, ex, _cls, member, file, line);
}
