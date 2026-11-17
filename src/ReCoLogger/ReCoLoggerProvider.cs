using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Facade = ReCoLogger.Log;

namespace ReCoLogger;

/// <summary>
/// Bridges Microsoft.Extensions.Logging into ReCoLogger: everything written through
/// <c>ILogger</c> / <c>ILogger&lt;T&gt;</c> lands in the same sinks (console, logcat, file, live list)
/// and can be filtered with <see cref="LogFilter"/>.
/// </summary>
public sealed class ReCoLoggerProvider : ILoggerProvider
{
    readonly ConcurrentDictionary<string, ReCoMsLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, static n => new ReCoMsLogger(n));

    public void Dispose() => Facade.Flush();
}

/// <summary>ILogger implementation that forwards records to the static <see cref="ReCoLogger.Log"/> core.</summary>
sealed class ReCoMsLogger : ILogger
{
    static readonly Assembly Self = typeof(ReCoMsLogger).Assembly;

    readonly string _fullCategory;
    readonly string _category;

    public ReCoMsLogger(string categoryName)
    {
        _fullCategory = categoryName;
        _category = ShortName(categoryName);
    }

    /// <summary>ILogger scopes are mapped to ReCoLogger scopes, so they show up as {scope} in records.</summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => Facade.BeginScope(state.ToString() ?? string.Empty);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= Facade.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // The formatter resolves message templates: "User {Id} logged in" + args -> "User 5 logged in".
        string message = formatter(state, exception);
        if (message.Length == 0 && exception == null) return;

        (string Method, string File, int Line) caller = Facade.CaptureCallerInfo ? CaptureCaller() : ("?", "", 0);
        Facade.Write(logLevel, message, exception, _category, caller.Method, caller.File, caller.Line, eventId, _fullCategory);
    }

    /// <summary>
    /// ILogger does not pass caller info, so walk the stack to the first frame outside the logging
    /// plumbing. This is the code that called logger.LogXxx(...).
    /// </summary>
    static (string Method, string File, int Line) CaptureCaller()
    {
        try
        {
            var frames = new StackTrace(fNeedFileInfo: true).GetFrames();
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                if (method == null || type == null) continue;

                var ns = type.Namespace ?? string.Empty;
                if (type.Assembly == Self ||
                    ns == "System" ||
                    ns.StartsWith("System.", StringComparison.Ordinal) ||
                    ns.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal))
                    continue;

                return (CleanMethodName(method), frame.GetFileName() ?? string.Empty, frame.GetFileLineNumber());
            }
        }
        catch { /* stack inspection can fail on trimmed/AOT builds */ }
        return ("?", string.Empty, 0);
    }

    /// <summary>Turns compiler-generated names ("MoveNext" of async state machines, lambdas) into the real method name.</summary>
    static string CleanMethodName(MethodBase method)
    {
        var name = method.Name;

        // async methods: the frame is "MoveNext" on a state machine type called "<Foo>d__5"
        if (name == "MoveNext" && method.DeclaringType is { Name: var typeName } && typeName.StartsWith('<'))
            name = typeName;

        // "<Foo>b__0_0" (lambda) or "<Foo>d__5" (async) -> "Foo"
        int open = name.IndexOf('<');
        int close = name.IndexOf('>');
        return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
    }

    /// <summary>"MyApp.Services.OrderService" -> "OrderService" (generic arguments are kept).</summary>
    static string ShortName(string category)
    {
        int generic = category.IndexOf('<');
        var head = generic >= 0 ? category[..generic] : category;
        int dot = head.LastIndexOf('.');
        var name = dot >= 0 ? head[(dot + 1)..] : head;
        return generic >= 0 ? name + category[generic..] : name;
    }
}

/// <summary>DI registration for ReCoLogger.</summary>
public static class ReCoLoggerExtensions
{
    /// <summary>
    /// Initializes ReCoLogger and registers it as an ILogger provider. Also registers
    /// <see cref="LogStore"/> (singleton) and <see cref="LiveLogView"/> (transient) so view models can inject them.
    /// Call it first, before anything logs: options are applied only if the logger is not initialized yet.
    /// </summary>
    public static ILoggingBuilder AddReCoLogger(this ILoggingBuilder builder, Action<LogOptions>? configure = null)
    {
        var options = new LogOptions();
        configure?.Invoke(options);
        Facade.Init(options);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ReCoLoggerProvider>());
        builder.Services.TryAddSingleton(Facade.Store);
        builder.Services.TryAddTransient<LiveLogView>();
        return builder;
    }
}
