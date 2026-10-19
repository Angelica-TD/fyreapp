using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.Services.Properties;
using FyreApp.Services.Timeline;
using FyreApp.Tests.Helpers;
using Xunit;

namespace FyreApp.Tests.Services;

// The Uptick-style property page: each tab loads what it shows
public class PropertyPageServiceTests
{
    private static DateTime Utc(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

    private static async Task<Property> SeedAsync(AppDbContext db)
    {
        var site = new Property { Name = "Ezfurn", ExternalId = "P-0391", Created = Utc("2022-02-01 09:18"), Client = new Client { Name = "Ezfurn" } };
        var hoseReel = new AssetType { Name = "Fire Hose Reel" };
        var extinguisher = new AssetType { Name = "Fire Extinguisher" };
        db.Assets.AddRange(
            new Asset { Property = site, Name = "020 - Fire Hose Reel", InspectionOrder = 20, AssetTypes = { hoseReel } },
            new Asset { Property = site, Name = "018 - DCP 4.5KG", InspectionOrder = 0, AssetTypes = { extinguisher } },
            new Asset { Property = site, Name = "Old extinguisher", IsActive = false, AssetTypes = { extinguisher } });
        db.Remarks.AddRange(
            new Remark { Property = site, Severity = 2, SeverityLabel = "Non-conformance", Active = true, RaisedUtc = Utc("2026-10-07 04:00"), Description = "Signage missing" },
            new Remark { Property = site, Severity = 0, SeverityLabel = "Informational", Active = true, RaisedUtc = Utc("2026-10-01 04:00") },
            new Remark { Property = site, Severity = 10, SeverityLabel = "Critical defect", Active = false, RaisedUtc = Utc("2026-01-01 04:00"), InactiveUtc = Utc("2026-02-01 04:00") });
        db.ClientTasks.Add(new ClientTask
        {
            Client = site.Client, Property = site, Title = "PM2026/04 Servicing", Ref = "T-31713",
            CreatedUtc = Utc("2026-04-01 00:00"), CompletedUtc = Utc("2026-04-08 07:30"), Status = ClientTaskStatus.Completed
        });
        await db.SaveChangesAsync();
        return site;
    }

    private static PropertyPageService Sut(AppDbContext db) => new(db, new RoutineListService(db), new TimelineService(db));

    [Fact]
    public async Task Summary_ShowsTheMostSevereOpenRemark()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);

        var page = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery(null));

        Assert.Equal("property", page!.Tab);
        // The critical defect is closed, so non-conformance is the most severe of the 2 open ones
        Assert.Equal((2, "Non-conformance"), (page.OpenRemarks, page.MostSevereRemark!.SeverityLabel));
    }

    [Fact]
    public async Task Assets_DefaultToActive_InInspectionOrder_AndFilterByType()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);

        var page = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("assets"));
        Assert.Equal(new[] { "018 - DCP 4.5KG", "020 - Fire Hose Reel" }, page!.Assets.Select(a => a.Name));
        Assert.Equal(new[] { "Fire Extinguisher", "Fire Hose Reel" }, page.AssetTypes.Select(t => t.Name));

        var extinguisherId = page.AssetTypes.Single(t => t.Name == "Fire Extinguisher").Id;
        var any = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("assets", AssetActive: "any", AssetType: extinguisherId));
        Assert.Equal(new[] { "018 - DCP 4.5KG", "Old extinguisher" }, any!.Assets.Select(a => a.Name));
    }

    [Fact]
    public async Task Remarks_OpenOnlyByDefault_MostSevereFirst()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);

        var open = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("remarks"));
        Assert.Equal(new int?[] { 2, 0 }, open!.Remarks.Select(d => d.Severity));

        var all = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("remarks", ShowClosedRemarks: true));
        Assert.Equal(new int?[] { 10, 2, 0 }, all!.Remarks.Select(d => d.Severity));
    }

    [Fact]
    public async Task Timeline_IsTheChangeLog_NewestFirst()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);
        db.TimelineEvents.AddRange(
            new TimelineEvent { PropertyId = site.Id, UptickId = 1, WhenUtc = Utc("2022-02-01 09:18"), Author = "Quentin McCarthy", Html = "Property was created." },
            new TimelineEvent { PropertyId = site.Id, WhenUtc = Utc("2026-10-13 04:24"), Author = "Wes Speedon", Field = "Name", Value = "Ezfurn" },
            new TimelineEvent { PropertyId = site.Id, WhenUtc = Utc("2026-10-13 04:26"), Author = "Wes Speedon", IsNote = true, Text = "Gate code changed" });
        await db.SaveChangesAsync();

        var page = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("property"));
        Assert.Equal(new[] { "Gate code changed", null, null }, page!.Timeline.Select(e => e.Text));
        Assert.Equal("Property was created.", page.Timeline.Last().Html);
        // Tasks, reports and remarks aren't on it, as in Uptick
        Assert.Equal(3, page.Timeline.Count);

        var notes = await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("property", NotesOnly: true));
        Assert.Equal("Gate code changed", Assert.Single(notes!.Timeline).Text);
    }

    [Fact]
    public async Task UnknownTab_ShowsTheProperty_AndUnknownPropertyIsNull()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);

        Assert.Equal("property", (await Sut(db).LoadAsync(site.Id, new PropertyPageQuery("nonsense")))!.Tab);
        Assert.Null(await Sut(db).LoadAsync(999, new PropertyPageQuery(null)));
    }
}
