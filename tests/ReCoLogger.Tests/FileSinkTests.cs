using System.Text.RegularExpressions;

namespace ReCoLogger.Tests;

public class FileSinkTests
{
    [Fact]
    public void Creates_a_file_named_after_the_session_start()
    {
        using var dir = new TempDir();
        using var sink = new FileSink(dir.Path);

        var name = Path.GetFileName(sink.CurrentFile);

        Assert.Matches(new Regex(@"^log_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d+)?\.txt$"), name);
        Assert.True(File.Exists(sink.CurrentFile));
    }

    [Fact]
    public void Writes_formatted_records_in_order()
    {
        using var dir = new TempDir();
        string file;
        var first = TestEntries.Make(message: "one");
        var second = TestEntries.Make(message: "two");

        using (var sink = new FileSink(dir.Path))
        {
            file = sink.CurrentFile;
            sink.Write(first);
            sink.Write(second);
        } // Dispose drains the queue and flushes

        Assert.Equal(new string?[] { first.Format(), second.Format() }, File.ReadAllLines(file));
    }

    [Fact]
    public void Flush_makes_pending_records_visible_before_dispose()
    {
        using var dir = new TempDir();
        using var sink = new FileSink(dir.Path);

        sink.Write(TestEntries.Make(message: "visible"));
        sink.Flush();

        Assert.Contains("visible", File.ReadAllText(sink.CurrentFile));
    }

    [Fact]
    public void Keeps_only_the_requested_number_of_sessions_including_the_new_one()
    {
        using var dir = new TempDir();
        for (int i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(dir.Path, $"log_2000-01-01_00-00-0{i}.txt"), "old");

        using var sink = new FileSink(dir.Path, keepSessions: 3);

        var names = Directory.GetFiles(dir.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(3, names.Count);
        Assert.Equal("log_2000-01-01_00-00-04.txt", names[0]);
        Assert.Equal("log_2000-01-01_00-00-05.txt", names[1]);
        Assert.Equal(Path.GetFileName(sink.CurrentFile), names[2]);
    }

    [Fact]
    public void PruneOld_ignores_files_that_are_not_session_logs()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "notes.txt"), "keep me");
        File.WriteAllText(Path.Combine(dir.Path, "log_2000-01-01_00-00-01.txt"), "old");

        FileSink.PruneOld(dir.Path, keep: 0);

        Assert.True(File.Exists(Path.Combine(dir.Path, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "log_2000-01-01_00-00-01.txt")));
    }

    [Fact]
    public void Two_sessions_started_in_the_same_second_do_not_overwrite_each_other()
    {
        using var dir = new TempDir();

        string a, b;
        using (var s1 = new FileSink(dir.Path, keepSessions: 5)) { a = s1.CurrentFile; s1.Write(TestEntries.Make(message: "first")); }
        using (var s2 = new FileSink(dir.Path, keepSessions: 5)) { b = s2.CurrentFile; s2.Write(TestEntries.Make(message: "second")); }

        Assert.NotEqual(a, b);
        Assert.Contains("first", File.ReadAllText(a));
        Assert.Contains("second", File.ReadAllText(b));
    }
}
