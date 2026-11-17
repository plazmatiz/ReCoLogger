using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

public class LogFilterTests
{
    static LogEntry Entry(LogLevel level = LogLevel.Information, string message = "hello", string cls = "Svc",
        DateTime? time = null, string? category = null, Exception? ex = null)
        => TestEntries.Make(level: level, message: message, cls: cls, time: time, category: category, ex: ex);

    [Fact]
    public void Default_filter_matches_everything()
    {
        var f = new LogFilter();

        Assert.True(f.Matches(Entry(LogLevel.Trace)));
        Assert.True(f.Matches(Entry(LogLevel.Critical)));
    }

    [Fact]
    public void Level_range_is_inclusive()
    {
        var f = new LogFilter { MinLevel = LogLevel.Warning, MaxLevel = LogLevel.Error };

        Assert.False(f.Matches(Entry(LogLevel.Information)));
        Assert.True(f.Matches(Entry(LogLevel.Warning)));
        Assert.True(f.Matches(Entry(LogLevel.Error)));
        Assert.False(f.Matches(Entry(LogLevel.Critical)));
    }

    [Fact]
    public void Time_range_is_inclusive()
    {
        var t = new DateTime(2026, 11, 16, 12, 0, 0);
        var f = new LogFilter { From = t, To = t.AddHours(1) };

        Assert.False(f.Matches(Entry(time: t.AddSeconds(-1))));
        Assert.True(f.Matches(Entry(time: t)));
        Assert.True(f.Matches(Entry(time: t.AddHours(1))));
        Assert.False(f.Matches(Entry(time: t.AddHours(1).AddSeconds(1))));
    }

    [Fact]
    public void Text_is_case_insensitive_and_also_searches_the_exception()
    {
        var f = new LogFilter { Text = "TIMEOUT" };

        Assert.True(f.Matches(Entry(message: "request timeout after 5s")));
        Assert.True(f.Matches(Entry(message: "failed", ex: new TimeoutException("timeout"))));
        Assert.False(f.Matches(Entry(message: "all good")));
    }

    [Fact]
    public void ClassName_must_match_exactly_ignoring_case()
    {
        var f = new LogFilter { ClassName = "svc" };

        Assert.True(f.Matches(Entry(cls: "Svc")));
        Assert.False(f.Matches(Entry(cls: "Svc2")));
    }

    [Fact]
    public void ExcludedCategories_hide_by_prefix_but_never_hide_records_without_category()
    {
        var f = new LogFilter { ExcludedCategories = ["Microsoft", "System.Net"] };

        Assert.False(f.Matches(Entry(category: "Microsoft.Hosting.Lifetime")));
        Assert.False(f.Matches(Entry(category: "system.net.http.HttpClient")));
        Assert.True(f.Matches(Entry(category: "MyApp.OrderService")));
        Assert.True(f.Matches(Entry(category: null)));
    }

    [Fact]
    public void FrameworkCategories_hide_Microsoft_and_System()
    {
        var f = new LogFilter { ExcludedCategories = LogFilter.FrameworkCategories };

        Assert.False(f.Matches(Entry(category: "Microsoft.EntityFrameworkCore.Database.Command")));
        Assert.False(f.Matches(Entry(category: "System.Net.Http.HttpClient.Default")));
        Assert.True(f.Matches(Entry(category: "MyApp.Services.OrderService")));
    }

    [Fact]
    public void IncludeILogger_false_hides_every_categorized_record()
    {
        var f = new LogFilter { IncludeILogger = false };

        Assert.False(f.Matches(Entry(category: "MyApp.OrderService")));
        Assert.True(f.Matches(Entry(category: null)));
    }

    [Fact]
    public void Changed_is_raised_only_when_a_value_really_changes()
    {
        var f = new LogFilter();
        int raised = 0;
        f.Changed += () => raised++;

        f.MinLevel = LogLevel.Warning;
        f.MinLevel = LogLevel.Warning; // same value
        f.Text = "x";
        f.IncludeILogger = false;
        f.ExcludedCategories = ["Microsoft"];

        Assert.Equal(4, raised);
    }

    /// <summary>
    /// The same filter must give the same answer for a live entry and for the text record of that entry
    /// (this is what makes LogFiles.ReadLast(n, filter) consistent with the live list).
    /// </summary>
    [Fact]
    public void Live_and_file_filtering_agree()
    {
        var entries = new[]
        {
            Entry(LogLevel.Trace, "trace msg"),
            Entry(LogLevel.Warning, "hello world", cls: "Other"),
            Entry(LogLevel.Error, "boom", category: "Microsoft.Hosting"),
            Entry(LogLevel.Information, "hello", category: "My.App.Svc"),
            Entry(LogLevel.Critical, "late", time: new DateTime(2026, 11, 17, 0, 0, 0)),
            Entry(LogLevel.Error, "failed", ex: new InvalidOperationException("timeout while connecting")),
        };

        var filters = new[]
        {
            new LogFilter(),
            new LogFilter { MinLevel = LogLevel.Warning },
            new LogFilter { MaxLevel = LogLevel.Information },
            new LogFilter { Text = "HELLO" },
            new LogFilter { Text = "timeout" },
            new LogFilter { ClassName = "Svc" },
            new LogFilter { ExcludedCategories = ["Microsoft"] },
            new LogFilter { IncludeILogger = false },
            new LogFilter { From = new DateTime(2026, 11, 17) },
            new LogFilter { To = new DateTime(2026, 11, 16, 23, 0, 0) },
        };

        foreach (var filter in filters)
            foreach (var entry in entries)
                Assert.Equal(filter.Matches(entry), filter.MatchesRecord(entry.Format()));
    }

    [Fact]
    public void Text_filter_for_records_ignores_the_header()
    {
        var f = new LogFilter { Text = "Svc" };

        // "Svc" is the class name (header), not part of the message.
        Assert.False(f.MatchesRecord(Entry(message: "hello", cls: "Svc").Format()));
        Assert.True(f.MatchesRecord(Entry(message: "from Svc", cls: "Other").Format()));
    }
}
