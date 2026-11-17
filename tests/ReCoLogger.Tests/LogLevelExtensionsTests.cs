using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

public class LogLevelExtensionsTests
{
    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void Short_and_TryParseShort_round_trip(LogLevel level, string code)
    {
        Assert.Equal(code, level.Short());

        Assert.True(LogLevelExtensions.TryParseShort(code, out var parsed));
        Assert.Equal(level, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("inf")] // codes are upper-case
    public void TryParseShort_rejects_unknown_codes(string code)
    {
        Assert.False(LogLevelExtensions.TryParseShort(code, out var level));
        Assert.Equal(LogLevel.None, level);
    }
}
