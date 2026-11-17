using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

public class LogEntryTests
{
    [Fact]
    public void Format_has_the_documented_layout()
    {
        var e = TestEntries.Make();

        Assert.Equal("2026-11-16 21:53:01.123 [INF] [T05] Svc.Run:42 | hello", e.Format());
    }

    [Fact]
    public void Source_omits_the_line_when_unknown()
    {
        var e = TestEntries.Make(line: 0);

        Assert.Equal("Svc.Run", e.Source);
        Assert.Equal("2026-11-16 21:53:01.123 [INF] [T05] Svc.Run | hello", e.Format());
    }

    [Fact]
    public void Format_includes_event_id_category_and_scope_in_order()
    {
        var e = TestEntries.Make(eventId: new EventId(7), category: "My.Cat", scope: "A > B");

        Assert.Equal("2026-11-16 21:53:01.123 [INF] [T05] Svc.Run:42 #7 @My.Cat {A > B} | hello", e.Format());
    }

    [Fact]
    public void Spaces_in_category_are_replaced_so_the_header_stays_parseable()
    {
        var e = TestEntries.Make(category: "My Cat");

        Assert.Contains(" @My_Cat ", e.Format().Split(" | ")[0] + " ");
    }

    [Fact]
    public void Scope_separator_is_sanitized()
    {
        var e = TestEntries.Make(scope: "a | b");

        Assert.Contains("{a / b}", e.Format());
    }

    [Fact]
    public void Multiline_messages_are_indented_by_four_spaces()
    {
        var e = TestEntries.Make(message: "first\nsecond\r\nthird");

        Assert.EndsWith("| first\n    second\n    third", e.Format());
    }

    [Fact]
    public void Exception_is_appended_on_indented_lines()
    {
        var e = TestEntries.Make(level: LogLevel.Error, ex: new InvalidOperationException("boom"));

        var text = e.Format();

        Assert.Contains("\n    System.InvalidOperationException: boom", text);
        Assert.StartsWith("2026-11-16 21:53:01.123 [ERR]", text);
    }
}
