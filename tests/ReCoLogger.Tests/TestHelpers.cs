using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

/// <summary>Factory for LogEntry objects with sensible defaults.</summary>
internal static class TestEntries
{
    public static readonly DateTime DefaultTime = new(2026, 11, 16, 21, 53, 1, 123);

    public static LogEntry Make(
        LogLevel level = LogLevel.Information,
        string message = "hello",
        string cls = "Svc",
        string method = "Run",
        int line = 42,
        DateTime? time = null,
        string? category = null,
        string? scope = null,
        Exception? ex = null,
        EventId eventId = default,
        int thread = 5)
        => new()
        {
            Timestamp = time ?? DefaultTime,
            Level = level,
            Message = message,
            ClassName = cls,
            MethodName = method,
            Line = line,
            ThreadId = thread,
            Exception = ex,
            Scope = scope,
            EventId = eventId,
            Category = category,
        };
}

/// <summary>Gives a test an isolated temp folder that is deleted afterwards.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "recologger-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Gives a test a freshly initialized static logger writing to an isolated folder
/// (no console / debug / logcat output). Shuts the logger down on dispose.
/// </summary>
internal sealed class LogTestHost : IDisposable
{
    readonly TempDir _dir = new();

    public string Dir => _dir.Path;

    public LogTestHost(Action<LogOptions>? configure = null)
    {
        Log.Shutdown();
        Log.Init(CreateOptions(Dir, configure));
        Log.Store.Clear(); // drop the "Session started" record
    }

    public static LogOptions CreateOptions(string dir, Action<LogOptions>? configure = null)
    {
        var options = new LogOptions
        {
            AppName = "Tests",
            Directory = dir,
            ToConsole = false,
            ToDebugOutput = false,
            ToLogcat = false,
            CaptureUnhandledExceptions = false,
        };
        configure?.Invoke(options);
        return options;
    }

    public void Dispose()
    {
        Log.Shutdown();
        _dir.Dispose();
    }
}
