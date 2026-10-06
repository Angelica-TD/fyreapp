using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Assets;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Assets;
using Xunit;

namespace FyreApp.Tests.Services;

public class AssetListServiceTests
{
    private static async Task<(AssetType Extinguisher, AssetType Panel)> SeedAsync(AppDbContext db)
    {
        var client = new Client { Name = "Urbane Body Corporate", PrimaryContactName = "Karl Prodger" };
        var active = new Site { Name = "Cunningham Residences", ExternalId = "P-3332", Status = "ACTIVE", Client = client };
        var setup = new Site { Name = "New Site", ExternalId = "P-3338", Status = "SETUP", Client = client };
        var inactive = new Site { Name = "Old Site", ExternalId = "P-0010", Status = "INACTIVE", Client = client };
        var ext = new AssetType { Name = "Fire Extinguisher" };
        var panel = new AssetType { Name = "Fire Indicator Panel" };

        db.Assets.AddRange(
            new Asset { Name = "Ext 001", Ref = "001", ExternalId = "42476", Site = active, AssetTypes = { ext } },
            new Asset { Name = "Ext 100", Ref = "100", ExternalId = "42488", Site = active, AssetTypes = { ext } },
            new Asset { Name = "FIP", Ref = "1", ExternalId = "9999", Site = setup, AssetTypes = { panel } },
            new Asset { Name = "Removed", Ref = "002", ExternalId = "42477", Site = active, IsActive = false, AssetTypes = { ext } },
            new Asset { Name = "Old", Ref = "003", ExternalId = "100", Site = inactive, AssetTypes = { ext } },
            new Asset { Name = "Made in FyreApp", Ref = null, Site = active, AssetTypes = { panel } });
        await db.SaveChangesAsync();
        return (ext, panel);
    }

    [Fact]
    public async Task DefaultFilters_MatchUptick_NewestFirst()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        var result = await new AssetListService(db).SearchAsync(new AssetFilter());

        // Active assets at Active/Setup/On hold properties; FyreApp-made first, then Uptick IDs numerically descending
        Assert.Equal(new[] { "Made in FyreApp", "Ext 100", "Ext 001", "FIP" }, result.Items.Select(i => i.Name));
        Assert.Equal(("P-3332", "Karl Prodger"), (result.Items[1].PropertyRef, result.Items[1].ClientContact));
        Assert.Equal(2, result.Filter.ActiveCount);
    }

    [Fact]
    public async Task AssetTypeFilter_IsAndIsNot()
    {
        using var db = DbContextFactory.Create();
        var (_, panel) = await SeedAsync(db);
        var svc = new AssetListService(db);

        var panels = await svc.SearchAsync(new AssetFilter { AssetTypeIds = { panel.Id } });
        var notPanels = await svc.SearchAsync(new AssetFilter { AssetTypeIds = { panel.Id }, AssetTypeIsNot = true });

        Assert.Equal(new[] { "Made in FyreApp", "FIP" }, panels.Items.Select(i => i.Name));
        Assert.Equal(new[] { "Ext 100", "Ext 001" }, notPanels.Items.Select(i => i.Name));
        Assert.Equal(2, panels.AssetTypes.Count);
    }

    [Fact]
    public async Task ClearedFilters_ShowEverything_AndSearchNarrows()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var svc = new AssetListService(db);

        var all = await svc.SearchAsync(new AssetFilter { Active = null, PropertyStatus = new() });
        var oldSite = await svc.SearchAsync(new AssetFilter { Active = null, PropertyStatus = new(), Search = "p-0010" });

        Assert.Equal((6, 1), (all.Total, oldSite.Total));
        Assert.Equal(0, all.Filter.ActiveCount);
    }
}
