using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

public class LogStoreTests
{
    [Fact]
    public void Drops_the_oldest_records_above_capacity()
    {
        var store = new LogStore { Capacity = 3 };

        for (int i = 1; i <= 5; i++) store.Write(TestEntries.Make(message: $"m{i}"));

        Assert.Equal(new string?[] { "m3", "m4", "m5" }, store.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Clear_empties_the_list()
    {
        var store = new LogStore();
        store.Write(TestEntries.Make());

        store.Clear();

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Dispatch_defers_collection_changes_until_the_posted_action_runs()
    {
        var queue = new List<Action>();
        var store = new LogStore { Dispatch = queue.Add };

        store.Write(TestEntries.Make());

        Assert.Empty(store.Entries);
        Assert.Single(queue);

        queue[0]();

        Assert.Single(store.Entries);
    }
}

public class LiveLogViewTests
{
    [Fact]
    public void Starts_with_the_entries_that_already_pass_the_filter()
    {
        var store = new LogStore();
        store.Write(TestEntries.Make(LogLevel.Information, "a"));
        store.Write(TestEntries.Make(LogLevel.Error, "b"));

        using var view = new LiveLogView(store, new LogFilter { MinLevel = LogLevel.Error });

        Assert.Equal(new string?[] { "b" }, view.Items.Select(e => e.Message));
    }

    [Fact]
    public void Follows_new_records_and_applies_the_filter_to_them()
    {
        var store = new LogStore();
        using var view = new LiveLogView(store);
        view.Filter.MinLevel = LogLevel.Warning;

        store.Write(TestEntries.Make(LogLevel.Information, "ignored"));
        store.Write(TestEntries.Make(LogLevel.Warning, "kept"));

        Assert.Equal(new string?[] { "kept" }, view.Items.Select(e => e.Message));
    }

    [Fact]
    public void Rebuilds_when_the_filter_changes()
    {
        var store = new LogStore();
        store.Write(TestEntries.Make(LogLevel.Information, "info"));
        store.Write(TestEntries.Make(LogLevel.Error, "error"));
        using var view = new LiveLogView(store);
        Assert.Equal(2, view.Items.Count);

        view.Filter.MinLevel = LogLevel.Error;
        Assert.Equal(new string?[] { "error" }, view.Items.Select(e => e.Message));

        view.Filter.MinLevel = LogLevel.Trace;
        Assert.Equal(2, view.Items.Count);
    }

    [Fact]
    public void Removes_records_that_fall_out_of_the_store()
    {
        var store = new LogStore { Capacity = 2 };
        using var view = new LiveLogView(store);

        for (int i = 1; i <= 4; i++) store.Write(TestEntries.Make(message: $"m{i}"));

        Assert.Equal(new string?[] { "m3", "m4" }, view.Items.Select(e => e.Message));
    }

    [Fact]
    public void Stops_following_the_store_after_dispose()
    {
        var store = new LogStore();
        var view = new LiveLogView(store);

        view.Dispose();
        store.Write(TestEntries.Make());

        Assert.Empty(view.Items);
    }
}
