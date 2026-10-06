using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Clients;
using FyreApp.Services.Lists;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Lists;
using Xunit;

namespace FyreApp.Tests.Services;

// Uptick-style list pages: default filters and order
public class ListServiceTests
{
    private static DateTime Utc(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

    private static async Task<(Site Active, Site Inactive)> SeedAsync(AppDbContext db)
    {
        var client = new Client { Name = "Apex Mezzanines", PrimaryContactName = "Naomi" };
        var active = new Site { Name = "3/10 Expo Ct", ExternalId = "P-3338", Status = "ACTIVE", State = "QLD", Created = Utc("2026-10-01T02:31:24"), Client = client };
        var onHold = new Site { Name = "Paused", ExternalId = "P-3000", Status = "ONHOLD", Created = Utc("2025-01-01"), Client = client };
        var setup = new Site { Name = "1/8 Expansion St", ExternalId = "P-3337", Status = "SETUP", Created = Utc("2026-10-01T02:18:20"), Client = client };
        var inactive = new Site { Name = "Old", ExternalId = "P-0010", Status = "INACTIVE", Created = Utc("2021-01-01"), Client = client };
        db.Sites.AddRange(active, onHold, setup, inactive);
        await db.SaveChangesAsync();
        return (active, inactive);
    }

    [Fact]
    public async Task Properties_DefaultIsNotInactive_NewestFirst()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        var result = await new PropertyListService(db).SearchAsync(new PropertyFilter());

        Assert.Equal(new[] { "P-3338", "P-3337", "P-3000" }, result.Items.Select(p => p.Ref));
        Assert.Equal(("ACTIVE", "QLD", "Naomi"), (result.Items[0].Status, result.Items[0].State, result.Items[0].ClientContact));
        Assert.Equal(1, result.Filter.ActiveCount);
    }

    [Fact]
    public async Task Remarks_DefaultsMatchUptick()
    {
        using var db = DbContextFactory.Create();
        var (active, inactive) = await SeedAsync(db);
        var asset = new Asset { Name = "4 - DCP AB(E) 4.5KG", Site = active };
        var retired = new Asset { Name = "Retired", Site = active, IsActive = false };

        Defect D(string id, Site site, Asset? a, string severity, bool open = true) =>
            new() { ExternalId = id, Site = site, Asset = a, SeverityLabel = severity, Active = open, RemarkType = "00 - Asset Failed" };

        db.Defects.AddRange(
            D("54214", active, asset, "Non-conformance"),                 // shown
            D("54280", active, asset, "Critical defect"),                 // shown (newest)
            D("9999", active, asset, "Non-critical defect"),              // shown (numerically lowest)
            D("54300", active, asset, "Informational"),                   // severity not in default
            D("54301", active, asset, "Non-conformance", open: false),    // resolved
            D("54302", active, retired, "Non-conformance"),               // asset inactive
            D("54303", active, null, "Non-conformance"),                  // no asset
            D("54304", inactive, asset, "Non-conformance"));              // property inactive
        await db.SaveChangesAsync();

        var result = await new RemarkListService(db).SearchAsync(new RemarkFilter());

        Assert.Equal(new[] { "D-54280", "D-54214", "D-9999" }, result.Items.Select(r => r.Ref));
        Assert.Equal(4, result.Filter.ActiveCount);
        Assert.Contains("Informational", result.Severities);
    }

    [Fact]
    public async Task Reports_NoFilters_NewestUptickIdFirst()
    {
        using var db = DbContextFactory.Create();
        var (active, _) = await SeedAsync(db);
        db.ServiceReports.AddRange(
            new ServiceReport { ExternalId = "52331", Ref = "R-52331", Site = active, ReportType = "Service Report", Compliant = false },
            new ServiceReport { ExternalId = "9000", Ref = "R-09000", Site = active, ReportType = "Service Report", Compliant = true },
            new ServiceReport { ExternalId = "52362", Ref = "R-52362", Site = active, ReportType = "Service Report", Compliant = true });
        await db.SaveChangesAsync();

        var result = await new ReportListService(db).SearchAsync(new ReportFilter());

        Assert.Equal(new[] { "R-52362", "R-52331", "R-09000" }, result.Items.Select(r => r.Ref));
        Assert.Equal("Apex Mezzanines", result.Items[0].ClientName);
    }

    [Fact]
    public async Task Clients_DefaultActiveOnly_ByName()
    {
        using var db = DbContextFactory.Create();
        db.Clients.AddRange(
            new Client { Name = "Zeta", Active = true },
            new Client { Name = "Alpha", Active = true },
            new Client { Name = "Gone", Active = false });
        await db.SaveChangesAsync();
        var svc = new ClientService(db);

        var (activeTotal, active) = await svc.SearchAsync(null, true, 1, 50);
        var (anyTotal, _) = await svc.SearchAsync(null, null, 1, 50);

        Assert.Equal(new[] { "Alpha", "Zeta" }, active.Select(c => c.Name));
        Assert.Equal((2, 3), (activeTotal, anyTotal));
    }

    [Fact]
    public void Defect_DisplayRef_IsUptickStyle()
    {
        Assert.Equal("D-54214", new Defect { ExternalId = "54214" }.DisplayRef);
        Assert.Equal("FD-1001", new Defect { FyreRef = "FD-1001" }.DisplayRef);
    }
}
