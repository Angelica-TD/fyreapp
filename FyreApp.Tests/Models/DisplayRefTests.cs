using FyreApp.Models;
using Xunit;

namespace FyreApp.Tests.Models;

public class DisplayRefTests
{
    [Theory]
    [InlineData("6214", null, "6214")]       // imported: Uptick ID
    [InlineData(null, "C-1001", "C-1001")]   // made in FyreApp: FyreApp ref
    [InlineData("", "C-1001", "C-1001")]     // blank Uptick ID counts as none
    [InlineData("6214", "C-1001", "6214")]   // linked to Uptick later: Uptick ID wins
    [InlineData(null, null, null)]
    public void Client_PrefersUptickId_ThenFyreRef(string? externalId, string? fyreRef, string? expected)
    {
        var client = new Client { ExternalId = externalId, FyreRef = fyreRef };

        Assert.Equal(expected, client.DisplayRef);
    }

    [Theory]
    [InlineData("R-51081", "98765", null, "R-51081")]   // Uptick's printed report ref
    [InlineData(null, "98765", null, "98765")]
    [InlineData(null, null, "SR-1001", "SR-1001")]
    public void ServiceReport_PrefersReportRef_ThenUptickId_ThenFyreRef(
        string? reportRef, string? externalId, string? fyreRef, string? expected)
    {
        var report = new ServiceReport { Ref = reportRef, ExternalId = externalId, FyreRef = fyreRef };

        Assert.Equal(expected, report.DisplayRef);
    }

    [Fact]
    public void Task_ShowsFyreRef()
    {
        Assert.Equal("T-1001", new ClientTask { FyreRef = "T-1001" }.DisplayRef);
    }
}
