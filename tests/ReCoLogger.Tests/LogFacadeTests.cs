using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

public class LogFacadeTests
{
    [Fact]
    public void Writes_to_the_store_with_class_method_and_line()
    {
        using var host = new LogTestHost();

        Log.Information("hello");

        var e = Assert.Single(Log.Store.Entries);
        Assert.Equal("hello", e.Message);
        Assert.Equal(LogLevel.Information, e.Level);
        Assert.Equal(nameof(LogFacadeTests), e.ClassName); // class comes from the source file name
        Assert.Equal(nameof(Writes_to_the_store_with_class_method_and_line), e.MethodName);
        Assert.True(e.Line > 0);
        Assert.Null(e.Category);
    }

    [Fact]
    public void MinLevel_drops_lower_records()
    {
        using var host = new LogTestHost(o => o.MinLevel = LogLevel.Warning);

        Log.Information("dropped");
        Log.Warning("kept 1");
        Log.Error("kept 2");

        Assert.Equal(new string?[] { "kept 1", "kept 2" }, Log.Store.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Exceptions_are_attached_to_the_record()
    {
        using var host = new LogTestHost();
        var ex = new InvalidOperationException("boom");

        Log.Error("failed", ex);

        Assert.Same(ex, Assert.Single(Log.Store.Entries).Exception);
    }

    [Fact]
    public void ForClass_uses_the_given_class_name()
    {
        using var host = new LogTestHost();

        Log.For<LogFacadeTests>().Warning("typed");
        Log.For("Custom").Information("named");

        Assert.Equal(new string?[] { "LogFacadeTests", "Custom" }, Log.Store.Entries.Select(e => e.ClassName));
    }

    [Fact]
    public void Scopes_nest_and_unwind()
    {
        using var host = new LogTestHost();

        using (Log.BeginScope("Order 42"))
        {
            using (Log.BeginScope("Payment"))
                Log.Information("inner");
            Log.Information("outer");
        }
        Log.Information("none");

        Assert.Equal(new string?[] { "Order 42 > Payment", "Order 42", null }, Log.Store.Entries.Select(e => e.Scope));
    }

    [Fact]
    public async Task Scopes_flow_across_await()
    {
        using var host = new LogTestHost();

        using (Log.BeginScope("Job"))
        {
            await Task.Delay(1);
            Log.Information("after await");
        }

        Assert.Equal("Job", Assert.Single(Log.Store.Entries).Scope);
    }

    [Fact]
    public void Measure_logs_the_elapsed_time_when_disposed()
    {
        using var host = new LogTestHost();

        using (Log.Measure("Work")) { }

        var e = Assert.Single(Log.Store.Entries);
        Assert.Equal(LogLevel.Debug, e.Level);
        Assert.StartsWith("Work took ", e.Message);
        Assert.EndsWith(" ms", e.Message);
    }

    [Fact]
    public void Records_are_persisted_and_can_be_read_back()
    {
        using var host = new LogTestHost();

        Log.Information("persist me");
        var last = LogFiles.ReadLast(1); // flushes the running logger first

        Assert.Contains("persist me", Assert.Single(last));
    }

    [Fact]
    public void Only_the_configured_number_of_sessions_is_kept_on_disk()
    {
        using var host = new LogTestHost();

        for (int i = 0; i < 5; i++)
        {
            Log.Shutdown();
            Log.Init(LogTestHost.CreateOptions(host.Dir, o => o.KeepSessions = 3));
            Log.Information($"session {i}");
        }

        Assert.Equal(3, Directory.GetFiles(host.Dir, "log_*.txt").Length);
    }

    [Fact]
    public void Store_capacity_comes_from_the_options()
    {
        using var host = new LogTestHost(o => o.StoreCapacity = 3);

        for (int i = 1; i <= 5; i++) Log.Information($"m{i}");

        Assert.Equal(new string?[] { "m3", "m4", "m5" }, Log.Store.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Logging_after_shutdown_is_ignored()
    {
        using var host = new LogTestHost();

        Log.Shutdown();
        Log.Information("late");

        Assert.DoesNotContain(Log.Store.Entries, e => e.Message == "late");
    }

    [Fact]
    public void Custom_sinks_receive_records()
    {
        using var host = new LogTestHost();
        var sink = new ListSink();
        Log.AddSink(sink);

        Log.Information("to custom sink");

        Assert.Equal("to custom sink", Assert.Single(sink.Entries).Message);
    }

    sealed class ListSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = new();
        public void Write(LogEntry entry) => Entries.Add(entry);
        public void Dispose() { }
    }
}
