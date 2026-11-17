using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

public class LogFilesTests
{
    static void WriteSession(string dir, params LogEntry[] entries)
    {
        using var sink = new FileSink(dir, keepSessions: 10);
        foreach (var e in entries) sink.Write(e);
    }

    [Fact]
    public void GetSessionFiles_is_empty_for_a_missing_folder()
    {
        Assert.Empty(LogFiles.GetSessionFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ReadLast_returns_the_last_n_records_in_chronological_order()
    {
        using var dir = new TempDir();
        WriteSession(dir.Path,
            TestEntries.Make(message: "one"),
            TestEntries.Make(message: "two"),
            TestEntries.Make(message: "three"));

        var last = LogFiles.ReadLast(2, directory: dir.Path);

        Assert.Equal(2, last.Count);
        Assert.EndsWith("| two", last[0]);
        Assert.EndsWith("| three", last[1]);
    }

    [Fact]
    public void A_multiline_record_counts_as_one_record()
    {
        using var dir = new TempDir();
        WriteSession(dir.Path,
            TestEntries.Make(message: "before"),
            TestEntries.Make(level: LogLevel.Error, message: "failed", ex: new InvalidOperationException("boom")),
            TestEntries.Make(message: "after"));

        var all = LogFiles.ReadAll(directory: dir.Path);

        Assert.Equal(3, all.Count);
        Assert.Contains("System.InvalidOperationException: boom", all[1]);
        Assert.Contains("\n    ", all[1]);
    }

    [Fact]
    public void ReadLast_spans_sessions_and_keeps_chronological_order()
    {
        using var dir = new TempDir();
        WriteSession(dir.Path, TestEntries.Make(message: "s1-a"), TestEntries.Make(message: "s1-b"));
        WriteSession(dir.Path, TestEntries.Make(message: "s2-a"));

        var last = LogFiles.ReadLast(2, directory: dir.Path);

        Assert.EndsWith("| s1-b", last[0]);
        Assert.EndsWith("| s2-a", last[1]);
    }

    [Fact]
    public void ReadLast_applies_the_filter_first_so_you_still_get_n_matching_records()
    {
        using var dir = new TempDir();
        WriteSession(dir.Path,
            TestEntries.Make(message: "app 1", category: "MyApp.Svc"),
            TestEntries.Make(message: "app 2", category: "MyApp.Svc"),
            TestEntries.Make(message: "noise 1", category: "Microsoft.Hosting"),
            TestEntries.Make(message: "noise 2", category: "Microsoft.Hosting"),
            TestEntries.Make(message: "noise 3", category: "System.Net.Http"));
        var filter = new LogFilter { ExcludedCategories = LogFilter.FrameworkCategories };

        var last = LogFiles.ReadLast(2, filter, dir.Path);

        Assert.Equal(2, last.Count);
        Assert.EndsWith("| app 1", last[0]);
        Assert.EndsWith("| app 2", last[1]);
    }

    [Fact]
    public void ReadAll_filters_by_level()
    {
        using var dir = new TempDir();
        WriteSession(dir.Path,
            TestEntries.Make(LogLevel.Information, "info"),
            TestEntries.Make(LogLevel.Error, "error"),
            TestEntries.Make(LogLevel.Critical, "critical"));

        var errors = LogFiles.ReadAll(new LogFilter { MinLevel = LogLevel.Error }, dir.Path);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ExportZip_contains_every_session_file()
    {
        using var dir = new TempDir();
        WriteSession(dir.Path, TestEntries.Make(message: "a"));
        WriteSession(dir.Path, TestEntries.Make(message: "b"));
        var zipPath = Path.Combine(dir.Path, "export.zip");

        LogFiles.ExportZip(zipPath, dir.Path);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(2, zip.Entries.Count);
        Assert.All(zip.Entries, e => Assert.StartsWith("log_", e.Name));
    }
}
