using FyreApp.Models;
using Xunit;

namespace FyreApp.Tests.Models;

public class DisplayRefTests
{
    [Theory]
    [InlineData(6214L, null, "6214")]        // migrated: Uptick ID
    [InlineData(null, "C-1001", "C-1001")]   // made in FyreApp: FyreApp ref
    [InlineData(6214L, "C-1001", "6214")]    // linked to Uptick later: Uptick ID wins
    [InlineData(null, null, null)]
    public void Client_PrefersUptickId_ThenFyreRef(long? uptickId, string? fyreRef, string? expected)
    {
        var client = new Client { UptickId = uptickId, FyreRef = fyreRef };

        Assert.Equal(expected, client.DisplayRef);
    }

    [Theory]
    [InlineData("R-51081", 98765L, null, "R-51081")]   // Uptick's printed report ref
    [InlineData(null, 98765L, null, "98765")]
    [InlineData(null, null, "SR-1001", "SR-1001")]
    public void Report_PrefersReportRef_ThenUptickId_ThenFyreRef(
        string? reportRef, long? uptickId, string? fyreRef, string? expected)
    {
        var report = new Report { Ref = reportRef, UptickId = uptickId, FyreRef = fyreRef };

        Assert.Equal(expected, report.DisplayRef);
    }

    [Fact]
    public void Task_ShowsFyreRef()
    {
        Assert.Equal("T-1001", new ClientTask { FyreRef = "T-1001" }.DisplayRef);
    }
}
