using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ReCoLogger;

/// <summary>Helpers for the compact 3-letter level codes used in log files.</summary>
public static class LogLevelExtensions
{
    /// <summary>Returns the 3-letter code of a level: TRC, DBG, INF, WRN, ERR or CRT.</summary>
    public static string Short(this LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };

    /// <summary>Parses a 3-letter level code. Returns false for unknown codes.</summary>
    public static bool TryParseShort(string code, out LogLevel level)
    {
        level = code switch
        {
            "TRC" => LogLevel.Trace,
            "DBG" => LogLevel.Debug,
            "INF" => LogLevel.Information,
            "WRN" => LogLevel.Warning,
            "ERR" => LogLevel.Error,
            "CRT" => LogLevel.Critical,
            _ => LogLevel.None,
        };
        return level != LogLevel.None;
    }
}

/// <summary>A single log record.</summary>
public sealed class LogEntry
{
    /// <summary>Local time at which the record was created.</summary>
    public required DateTime Timestamp { get; init; }

    public required LogLevel Level { get; init; }

    /// <summary>Fully formatted message (message templates are already resolved).</summary>
    public required string Message { get; init; }

    /// <summary>Class that produced the log (the short ILogger category for DI logs).</summary>
    public required string ClassName { get; init; }

    public required string MethodName { get; init; }

    /// <summary>Full path of the source file, empty if unknown.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>Source line number, 0 if unknown.</summary>
    public int Line { get; init; }

    public int ThreadId { get; init; }

    public Exception? Exception { get; init; }

    /// <summary>Scope chain, e.g. "Order 42 > Payment". Null when there is no active scope.</summary>
    public string? Scope { get; init; }

    /// <summary>ILogger event id (Id == 0 means "not set").</summary>
    public EventId EventId { get; init; }

    /// <summary>
    /// Full ILogger category, e.g. "Microsoft.EntityFrameworkCore.Database.Command".
    /// Null for records written through the static <c>Log</c> facade, so it also tells where a record came from.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>"Class.Method:42", or "Class.Method" when the line is unknown.</summary>
    public string Source => Line > 0 ? $"{ClassName}.{MethodName}:{Line}" : $"{ClassName}.{MethodName}";

    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>
    /// Formats the record as text, e.g.
    /// "2026-11-16 21:53:01.123 [INF] [T05] Class.Method:42 #7 @My.Category {scope} | message".
    /// Extra lines (multi-line messages, exceptions) are indented with 4 spaces so the
    /// file parser can tell where the next record begins. See docs/FILE_FORMAT.md.
    /// </summary>
    public string Format()
    {
        var sb = new StringBuilder(160);
        sb.Append(Timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture))
          .Append(" [").Append(Level.Short()).Append("] [T").Append(ThreadId.ToString("00")).Append("] ")
          .Append(Source);
        if (EventId.Id != 0) sb.Append(" #").Append(EventId.Id);
        if (Category != null) sb.Append(" @").Append(Category.Replace(' ', '_'));
        if (Scope != null) sb.Append(" {").Append(Scope.Replace(" | ", " / ")).Append('}');
        sb.Append(" | ").Append(Indent(Message));
        if (Exception != null) sb.Append("\n    ").Append(Indent(Exception.ToString()));
        return sb.ToString();
    }

    public override string ToString() => Format();

    static string Indent(string s) => s.ReplaceLineEndings("\n").Replace("\n", "\n    ");
}

/// <summary>
/// Log filter: level range, time range, text, class and ILogger category.
/// Changing any property raises <see cref="Changed"/>.
/// The same filter works for live entries (<see cref="LiveLogView"/>) and for records read back from files.
/// </summary>
public sealed class LogFilter
{
    LogLevel _min = LogLevel.Trace, _max = LogLevel.Critical;
    DateTime? _from, _to;
    string? _text, _class;
    string[] _excluded = [];
    bool _includeILogger = true;

    /// <summary>Raised whenever a filter property changes.</summary>
    public event Action? Changed;

    public LogLevel MinLevel { get => _min; set => Set(ref _min, value); }
    public LogLevel MaxLevel { get => _max; set => Set(ref _max, value); }

    /// <summary>Only records at or after this time.</summary>
    public DateTime? From { get => _from; set => Set(ref _from, value); }

    /// <summary>Only records at or before this time.</summary>
    public DateTime? To { get => _to; set => Set(ref _to, value); }

    /// <summary>Case-insensitive substring searched in the message (and exception text).</summary>
    public string? Text { get => _text; set => Set(ref _text, value); }

    /// <summary>Exact (case-insensitive) class name.</summary>
    public string? ClassName { get => _class; set => Set(ref _class, value); }

    /// <summary>
    /// ILogger category prefixes to hide, e.g. <c>["Microsoft", "System"]</c> hides all framework spam
    /// but keeps your own ILogger&lt;T&gt; logs. Prefix match, case-insensitive. Records from the static
    /// <c>Log</c> facade have no category and are never hidden by this.
    /// </summary>
    public string[] ExcludedCategories { get => _excluded; set => Set(ref _excluded, value ?? Array.Empty<string>()); }

    /// <summary>Set to false to hide every record that came through ILogger (own and framework).</summary>
    public bool IncludeILogger { get => _includeILogger; set => Set(ref _includeILogger, value); }

    /// <summary>Ready-made value for <see cref="ExcludedCategories"/> that hides Microsoft.* and System.* logs.</summary>
    public static string[] FrameworkCategories => ["Microsoft", "System"];

    void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
    }

    bool IsExcludedCategory(string category)
    {
        foreach (var prefix in _excluded)
            if (!string.IsNullOrEmpty(prefix) && category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Reads the "@Category" token from the header of a text record. The header is
    /// "date time [LVL] [Txx] Source [#id] [@category] [{scope}]", all space-separated, up to the first " | ".
    /// </summary>
    static string? ParseCategory(string rec)
    {
        int end = rec.IndexOf(" | ", StringComparison.Ordinal);
        var parts = (end >= 0 ? rec[..end] : rec).Split(' ');
        int i = 5;
        if (i < parts.Length && parts[i].StartsWith('#')) i++;
        return i < parts.Length && parts[i].StartsWith('@') ? parts[i][1..] : null;
    }

    /// <summary>Returns true if the live entry passes the filter.</summary>
    public bool Matches(LogEntry e)
    {
        if (e.Level < _min || e.Level > _max) return false;
        if (_from is { } f && e.Timestamp < f) return false;
        if (_to is { } t && e.Timestamp > t) return false;
        if (e.Category != null && (!_includeILogger || IsExcludedCategory(e.Category))) return false;
        if (!string.IsNullOrEmpty(_class) && !e.ClassName.Equals(_class, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(_text) &&
            !e.Message.Contains(_text, StringComparison.OrdinalIgnoreCase) &&
            !(e.Exception?.ToString().Contains(_text, StringComparison.OrdinalIgnoreCase) ?? false)) return false;
        return true;
    }

    /// <summary>Returns true if a raw text record read from a log file passes the filter.</summary>
    internal bool MatchesRecord(string rec)
    {
        if (rec.Length < 29 ||
            !DateTime.TryParseExact(rec.AsSpan(0, 23), LogEntry.TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts) ||
            !LogLevelExtensions.TryParseShort(rec.Substring(25, 3), out var lvl))
            return false;

        if (lvl < _min || lvl > _max) return false;
        if (_from is { } f && ts < f) return false;
        if (_to is { } t && ts > t) return false;
        if (!_includeILogger || _excluded.Length > 0)
        {
            var category = ParseCategory(rec);
            if (category != null && (!_includeILogger || IsExcludedCategory(category))) return false;
        }
        if (!string.IsNullOrEmpty(_class) && !rec.Contains($"] {_class}.", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(_text))
        {
            // Search only the message/exception part, not the header (class names, categories, ...).
            int sep = rec.IndexOf(" | ", StringComparison.Ordinal);
            var body = sep >= 0 ? rec.AsSpan(sep + 3) : rec.AsSpan();
            if (body.IndexOf(_text, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        return true;
    }
}

/// <summary>
/// In-memory ring buffer of the latest records, exposed as an ObservableCollection for realtime UI.
/// If <see cref="Dispatch"/> is set (e.g. Dispatcher.UIThread.Post), all collection changes go through it.
/// </summary>
public sealed class LogStore : ILogSink
{
    readonly ObservableCollection<LogEntry> _items = new();

    public LogStore() => Entries = new ReadOnlyObservableCollection<LogEntry>(_items);

    /// <summary>Observable list that is continuously updated with new records.</summary>
    public ReadOnlyObservableCollection<LogEntry> Entries { get; }

    /// <summary>Maximum number of records kept in memory; the oldest are dropped first.</summary>
    public int Capacity { get; set; } = 5000;

    /// <summary>Marshals collection changes to the UI thread. Required when Entries is bound to UI.</summary>
    public Action<Action>? Dispatch { get; set; }

    public void Write(LogEntry entry) => Run(() => Add(entry));

    public void Clear() => Run(() => _items.Clear());

    void Add(LogEntry e)
    {
        _items.Add(e);
        while (_items.Count > Capacity) _items.RemoveAt(0);
    }

    void Run(Action a)
    {
        if (Dispatch is { } d) d(a);
        else lock (_items) a();
    }

    public void Dispose() { }
}

/// <summary>
/// Filtered "live" list on top of <see cref="LogStore"/>. Change Filter.* from the UI thread and
/// <see cref="Items"/> updates by itself. Bind Items to a ListBox / ItemsControl.
/// </summary>
public sealed class LiveLogView : IDisposable
{
    readonly LogStore _store;

    public LiveLogView(LogStore store, LogFilter? filter = null)
    {
        _store = store;
        Filter = filter ?? new LogFilter();
        ((INotifyCollectionChanged)_store.Entries).CollectionChanged += OnStoreChanged;
        Filter.Changed += Rebuild;
        Rebuild();
    }

    public LogFilter Filter { get; }

    /// <summary>Records that currently pass the filter.</summary>
    public ObservableCollection<LogEntry> Items { get; } = new();

    void OnStoreChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (LogEntry x in e.NewItems!)
                    if (Filter.Matches(x)) Items.Add(x);
                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (LogEntry x in e.OldItems!) Items.Remove(x);
                break;
            default:
                Rebuild();
                break;
        }
    }

    void Rebuild()
    {
        Items.Clear();
        foreach (var x in _store.Entries)
            if (Filter.Matches(x)) Items.Add(x);
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_store.Entries).CollectionChanged -= OnStoreChanged;
        Filter.Changed -= Rebuild;
    }
}
