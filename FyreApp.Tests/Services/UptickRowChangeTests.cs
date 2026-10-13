using FyreApp.Services.Imports;
using Xunit;

namespace FyreApp.Tests.Services;

public class UptickRowChangeTests
{
    private static string Row(string updated, string name) => $"{{\"ID\":\"1\",\"Updated\":\"{updated}\",\"Name\":\"{name}\"}}";

    [Fact]
    public void SameRow_IsNotAnUpdate() =>
        Assert.False(UptickRowChange.ShouldUpdate(Row("2026-10-01 09:00:00", "Acme"), Row("2026-10-01 09:00:00", "Acme")));

    [Fact]
    public void ChangedInUptickSince_IsAnUpdate() =>
        Assert.True(UptickRowChange.ShouldUpdate(Row("2026-10-08 09:00:00", "Acme Pty Ltd"), Row("2026-10-01 09:00:00", "Acme")));

    [Fact]
    public void OlderExport_NeverOverwritesNewerData() =>
        Assert.False(UptickRowChange.ShouldUpdate(Row("2026-09-20 09:00:00", "Acme (old)"), Row("2026-10-01 09:00:00", "Acme")));

    // Some columns are worked out by Uptick from other records without touching "Updated"
    [Fact]
    public void SameUpdatedTime_ButDifferentValues_IsAnUpdate() =>
        Assert.True(UptickRowChange.ShouldUpdate(Row("2026-10-01 09:00:00", "Acme Pty Ltd"), Row("2026-10-01 09:00:00", "Acme")));

    [Fact]
    public void OnlyLineBreaksDiffer_IsNotAnUpdate() =>
        Assert.False(UptickRowChange.ShouldUpdate(Row("2026-10-01 09:00:00", "Unit 1\\nLevel 2"), Row("2026-10-01 09:00:00", "Unit 1\\r\\nLevel 2")));

    [Fact]
    public void ImportedBeforeTheRowWasKept_IsAnUpdate() =>
        Assert.True(UptickRowChange.ShouldUpdate(Row("2026-10-01 09:00:00", "Acme"), null));
}
