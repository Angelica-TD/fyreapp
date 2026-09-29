using FyreApp.Services.Imports;
using Xunit;

namespace FyreApp.Tests.Services;

public class UptickTimeTests
{
    [Theory]
    [InlineData("2026-09-25 11:55:36", 2026, 9, 25, 1, 55, 36)]   // Uptick export format
    [InlineData("2026-09-18 9:19:51", 2026, 9, 17, 23, 19, 51)]   // single-digit hour, crosses midnight
    [InlineData("25/09/2026 11:55", 2026, 9, 25, 1, 55, 0)]       // day/month CSV format
    [InlineData("2026-01-15 12:00:00", 2026, 1, 15, 2, 0, 0)]     // no daylight saving in Queensland
    public void ParseToUtc_LocalQueenslandTime_ConvertsToUtc(string input, int y, int mo, int d, int h, int mi, int s)
    {
        var result = UptickTime.ParseToUtc(input);

        Assert.Equal(new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc), result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Fact]
    public void ParseToUtc_ValueWithOffset_UsesThatOffset()
    {
        // Property contact exports carry an explicit offset (Sydney, daylight saving)
        var result = UptickTime.ParseToUtc("2021-02-11T12:02:24.108399+11:00");

        Assert.Equal(new DateTime(2021, 2, 11, 1, 2, 24, DateTimeKind.Utc), result!.Value.AddTicks(-(result.Value.Ticks % TimeSpan.TicksPerSecond)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void ParseToUtc_BlankOrInvalid_ReturnsNull(string? input)
    {
        Assert.Null(UptickTime.ParseToUtc(input));
    }
}
