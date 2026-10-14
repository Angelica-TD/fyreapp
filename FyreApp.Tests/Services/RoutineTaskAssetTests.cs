using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.Services.Tasks;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Tasks generated from routines list the assets they service (Work tab)
public class RoutineTaskAssetTests
{
    private const string Extinguishers = "10 - Portable and Wheeled Fire Extinguishers";
    private const string Doorsets = "12 - Passive Fire (Hinged and Pivoted Doorsets - Common)";

    private static RoutineFilter AnyFilter => new() { ClientActive = null, PropertyStatus = new(), Status = new() };

    // An asset type as Uptick's asset types export gives it: one applicable routine per line
    private static AssetType Type(string name, string applicable, string? excluded = null) => new()
    {
        Name = name,
        UptickData = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["Name"] = name, ["Applicable Routines"] = applicable, ["Excluded Routine Levels"] = excluded
        }.Where(kv => kv.Value != null).ToDictionary())
    };

    private static RoutineOccurrence Due(Site site, string routine, string group = "Servicing") => new()
    {
        Site = site, Routine = routine, ServiceGroup = group, Status = RoutineOccurrenceStatus.Pending,
        DueDate = DateTime.SpecifyKind(new DateTime(2026, 11, 30), DateTimeKind.Utc)
    };

    private static async Task<(Site Glory, Site Calce)> SeedAsync(AppDbContext db)
    {
        var glory = new Site { Name = "The Glory Well Church", ExternalId = "P-2241", Client = new Client { Name = "Sails Ministry" } };
        var calce = new Site { Name = "Calce Company", ExternalId = "P-0358", Client = new Client { Name = "Calce Company Pty Ltd" } };

        var extinguisher = Type("Fire Extinguisher", Extinguishers);
        var blanket = Type("Fire Blanket", "11 - Fire Blankets");
        var doorset = Type("Hinged and Pivoted Doorsets - Common", Doorsets + "\n12 - Passive Fire (Hinged and Pivoted Doorsets - SOU)",
            excluded: Doorsets + ": Six-monthly");

        db.Assets.AddRange(
            new Asset { Site = glory, Name = "FE Hall", Ref = "2", InspectionOrder = 2, Location = "Hall", AssetTypes = { extinguisher } },
            new Asset { Site = glory, Name = "FE Kitchen", Ref = "1", InspectionOrder = 1, Location = "Kitchen", AssetTypes = { extinguisher } },
            new Asset { Site = glory, Name = "FE Removed", Ref = "3", IsActive = false, AssetTypes = { extinguisher } },
            new Asset { Site = glory, Name = "Blanket", Ref = "4", AssetTypes = { blanket } },
            new Asset { Site = glory, Name = "Front door", Ref = "5", AssetTypes = { doorset } },
            new Asset { Site = calce, Name = "FE Office", Ref = "1", AssetTypes = { extinguisher } });

        db.RoutineOccurrences.AddRange(
            Due(glory, Extinguishers + ": Six-monthly"),
            Due(glory, Doorsets + ": Annual", "Passive"),
            Due(glory, Doorsets + ": Six-monthly", "Passive"),
            Due(calce, Extinguishers + ": Six-monthly"));
        await db.SaveChangesAsync();
        return (glory, calce);
    }

    [Fact]
    public async Task GeneratedTasks_ListTheActiveAssetsWhoseTypeTheRoutineAppliesTo()
    {
        using var db = DbContextFactory.Create();
        var (glory, calce) = await SeedAsync(db);
        var ids = await db.RoutineOccurrences.Select(o => o.Id).ToListAsync();

        await new RoutineService(db).GenerateTasksAsync(ids, allMatching: false, AnyFilter, null);

        var lines = await db.TaskAssets.Select(a => new { a.ClientTask.SiteId, a.ClientTask.Title, a.Asset.Name, a.Routine }).ToListAsync();

        // Glory's servicing task: its two active extinguishers, not the inactive one or the blanket (no blanket routine due)
        Assert.Equal(new[] { "FE Hall", "FE Kitchen" },
            lines.Where(l => l.SiteId == glory.Id && l.Title.EndsWith("Servicing")).Select(l => l.Name).OrderBy(n => n));
        // The doorset's annual routine, but not its excluded six-monthly level
        Assert.Equal((Doorsets + ": Annual", "Front door"),
            lines.Where(l => l.Title.EndsWith("Passive")).Select(l => (l.Routine, l.Name)).Single());
        // Each property's own assets only
        Assert.Equal("FE Office", lines.Single(l => l.SiteId == calce.Id).Name);
    }

    [Fact]
    public async Task WorkTab_ShowsTheTasksAssetsByRoutineInInspectionOrder()
    {
        using var db = DbContextFactory.Create();
        var (glory, _) = await SeedAsync(db);
        var ids = await db.RoutineOccurrences.Where(o => o.SiteId == glory.Id).Select(o => o.Id).ToListAsync();
        await new RoutineService(db).GenerateTasksAsync(ids, allMatching: false, AnyFilter, null);
        var task = await db.ClientTasks.SingleAsync(t => t.Title.EndsWith("Servicing"));

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "work"))!;

        Assert.Equal(new[] { "FE Kitchen", "FE Hall" }, vm.ServiceAssets.Select(a => a.Asset.Name));
        Assert.All(vm.ServiceAssets, a => Assert.Equal(Extinguishers + ": Six-monthly", a.Routine));
    }

    [Fact]
    public async Task WorkTab_WorksOutTheAssets_ForRoutineTasksGeneratedBeforeTheyWereSaved()
    {
        using var db = DbContextFactory.Create();
        var (glory, _) = await SeedAsync(db);
        var ids = await db.RoutineOccurrences.Where(o => o.SiteId == glory.Id).Select(o => o.Id).ToListAsync();
        await new RoutineService(db).GenerateTasksAsync(ids, allMatching: false, AnyFilter, null);
        var task = await db.ClientTasks.SingleAsync(t => t.Title.EndsWith("Servicing"));
        db.TaskAssets.RemoveRange(db.TaskAssets);
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "work"))!;

        Assert.Equal(new[] { "FE Kitchen", "FE Hall" }, vm.ServiceAssets.Select(a => a.Asset.Name));
    }

    [Fact]
    public void RoutineType_DropsTheLevel()
    {
        Assert.Equal(Extinguishers, RoutineAssets.RoutineType(Extinguishers + ": Six-monthly"));
        Assert.Equal("Backflow Prevention", RoutineAssets.RoutineType("Backflow Prevention"));
    }
}
