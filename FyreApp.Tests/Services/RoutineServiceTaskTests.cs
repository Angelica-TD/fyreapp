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
public class RoutineServiceTaskTests
{
    private const string Extinguishers = "10 - Portable and Wheeled Fire Extinguishers";
    private const string Doorsets = "12 - Passive Fire (Hinged and Pivoted Doorsets - Common)";

    private static RoutineFilter AnyFilter => new() { ClientActive = null, PropertyStatus = new(), Status = new() };

    // An asset type with Uptick's applicable routines (one per line) and excluded levels ("<routine>: <level>")
    private static AssetType Type(string name, string applicable, string? excluded = null)
    {
        var type = new AssetType { Name = name };
        foreach (var routine in applicable.Split('\n'))
            type.ApplicableRoutineTypes.Add(new RoutineServiceType { Name = routine });
        if (excluded != null)
            foreach (var level in excluded.Split('\n'))
            {
                var at = level.LastIndexOf(": ", StringComparison.Ordinal);
                type.ExcludedRoutineLevels.Add(new RoutineServiceLevel { Name = level[(at + 2)..], RoutineServiceType = new RoutineServiceType { Name = level[..at] } });
            }
        return type;
    }

    private static Routine Due(Property site, string routine, string group = "Servicing") => new()
    {
        Property = site, Name = routine, ServiceGroup = group, Status = RoutineStatus.Pending,
        DueDate = DateTime.SpecifyKind(new DateTime(2026, 11, 30), DateTimeKind.Utc)
    };

    private static async Task<(Property Glory, Property Calce)> SeedAsync(AppDbContext db)
    {
        var glory = new Property { Name = "The Glory Well Church", ExternalId = "P-2241", Client = new Client { Name = "Sails Ministry" } };
        var calce = new Property { Name = "Calce Company", ExternalId = "P-0358", Client = new Client { Name = "Calce Company Pty Ltd" } };

        var extinguisher = Type("Fire Extinguisher", Extinguishers);
        var blanket = Type("Fire Blanket", "11 - Fire Blankets");
        var doorset = Type("Hinged and Pivoted Doorsets - Common", Doorsets + "\n12 - Passive Fire (Hinged and Pivoted Doorsets - SOU)",
            excluded: Doorsets + ": Six-monthly");

        db.Assets.AddRange(
            new Asset { Property = glory, Name = "FE Hall", Ref = "2", InspectionOrder = 2, Location = "Hall", AssetTypes = { extinguisher } },
            new Asset { Property = glory, Name = "FE Kitchen", Ref = "1", InspectionOrder = 1, Location = "Kitchen", AssetTypes = { extinguisher } },
            new Asset { Property = glory, Name = "FE Removed", Ref = "3", IsActive = false, AssetTypes = { extinguisher } },
            new Asset { Property = glory, Name = "Blanket", Ref = "4", AssetTypes = { blanket } },
            new Asset { Property = glory, Name = "Front door", Ref = "5", AssetTypes = { doorset } },
            new Asset { Property = calce, Name = "FE Office", Ref = "1", AssetTypes = { extinguisher } });

        db.Routines.AddRange(
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
        var ids = await db.Routines.Select(o => o.Id).ToListAsync();

        await new RoutineListService(db).GenerateTasksAsync(ids, allMatching: false, AnyFilter, null);

        var lines = await db.ServiceTasks.Select(a => new { a.ClientTask.PropertyId, a.ClientTask.Title, a.Asset.Name, a.Routine }).ToListAsync();

        // Glory's servicing task: its two active extinguishers, not the inactive one or the blanket (no blanket routine due)
        Assert.Equal(new[] { "FE Hall", "FE Kitchen" },
            lines.Where(l => l.PropertyId == glory.Id && l.Title.EndsWith("Servicing")).Select(l => l.Name).OrderBy(n => n));
        // The doorset's annual routine, but not its excluded six-monthly level
        Assert.Equal((Doorsets + ": Annual", "Front door"),
            lines.Where(l => l.Title.EndsWith("Passive")).Select(l => (l.Routine, l.Name)).Single());
        // Each property's own assets only
        Assert.Equal("FE Office", lines.Single(l => l.PropertyId == calce.Id).Name);
    }

    [Fact]
    public async Task WorkTab_ShowsTheTasksAssetsByRoutineInInspectionOrder()
    {
        using var db = DbContextFactory.Create();
        var (glory, _) = await SeedAsync(db);
        var ids = await db.Routines.Where(o => o.PropertyId == glory.Id).Select(o => o.Id).ToListAsync();
        await new RoutineListService(db).GenerateTasksAsync(ids, allMatching: false, AnyFilter, null);
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
        var ids = await db.Routines.Where(o => o.PropertyId == glory.Id).Select(o => o.Id).ToListAsync();
        await new RoutineListService(db).GenerateTasksAsync(ids, allMatching: false, AnyFilter, null);
        var task = await db.ClientTasks.SingleAsync(t => t.Title.EndsWith("Servicing"));
        db.ServiceTasks.RemoveRange(db.ServiceTasks);
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "work"))!;

        Assert.Equal(new[] { "FE Kitchen", "FE Hall" }, vm.ServiceAssets.Select(a => a.Asset.Name));
    }

    [Fact]
    public void FromScope_ReadsTheRoutinesAnImportedTaskNames()
    {
        var routines = RoutineAssets.FromScope(7,
            "- 09 - Fire Hose Reels (Section 9): Annual\r\n" +
            "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Six-monthly\n" +
            "- 06 - Fire Detection (Fire Panels) (AS1851-2012 Section 6): Monthly\n" +
            "Quote to replace failed items as per service report.",
            ["09 - Fire Hose Reels", Extinguishers, "06 - Fire Detection", "06 - Fire Detection (Fire Panels)"]);

        Assert.Equal(new[]
        {
            new RoutineAssets.SiteRoutine(7, "09 - Fire Hose Reels: Annual", "09 - Fire Hose Reels"),
            new RoutineAssets.SiteRoutine(7, Extinguishers + ": Six-monthly", Extinguishers),
            new RoutineAssets.SiteRoutine(7, "06 - Fire Detection (Fire Panels): Monthly", "06 - Fire Detection (Fire Panels)")
        }, routines);
    }

    [Fact]
    public async Task WorkTab_ImportedRoutineTask_ListsTheAssetsForItsScopeOfWorks()
    {
        using var db = DbContextFactory.Create();
        var (glory, _) = await SeedAsync(db);
        db.RoutineServiceTypes.Add(new RoutineServiceType { Name = Extinguishers });
        var task = new ClientTask
        {
            ClientId = glory.ClientId, PropertyId = glory.Id, Title = "PM2026/10 Servicing", ExternalId = "46682",
            ScopeOfWorks = "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual"
        };
        db.ClientTasks.Add(task);
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "work"))!;

        Assert.Equal(new[] { Extinguishers + ": Annual" }, vm.ServiceRoutines);
        Assert.Equal(new[] { "FE Kitchen", "FE Hall" }, vm.ServiceAssets.Select(a => a.Asset.Name));
        Assert.Empty(vm.Routines); // not generated from routines
        // Its routine still heads the Work tab, without an ID or due date (none imported)
        Assert.Equal((Extinguishers + ": Annual", (Routine?)null), Assert.Single(vm.Visits));
    }

    [Fact]
    public async Task WorkTab_ImportedRoutineTask_ShowsItsRoutinesWhatWasServicedAndItsRemarks()
    {
        using var db = DbContextFactory.Create();
        var (glory, calce) = await SeedAsync(db);
        db.RoutineServiceTypes.Add(new RoutineServiceType { Name = Extinguishers });
        DateTime Utc(int month, int day) => DateTime.SpecifyKind(new DateTime(2026, month, day), DateTimeKind.Utc);
        var annual = new Routine { Property = glory, ExternalId = "49441", Name = Extinguishers + ": Annual", DueDate = Utc(10, 1), Status = RoutineStatus.Generated };
        db.Routines.AddRange(annual,
            new Routine { Property = glory, Name = Extinguishers + ": Annual", DueDate = Utc(11, 1), Status = RoutineStatus.Generated },
            new Routine { Property = calce, Name = Extinguishers + ": Annual", DueDate = Utc(10, 1), Status = RoutineStatus.Generated });
        var task = new ClientTask
        {
            ClientId = glory.ClientId, PropertyId = glory.Id, Title = "PM2026/10 Servicing", Ref = "T-46682", ExternalId = "46682", DueDateUtc = Utc(10, 31),
            ScopeOfWorks = "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual"
        };
        db.ClientTasks.Add(task);
        var kitchen = await db.Assets.SingleAsync(a => a.Name == "FE Kitchen");
        var hall = await db.Assets.SingleAsync(a => a.Name == "FE Hall");
        kitchen.LastServiceDate = new DateTime(2026, 10, 14);
        kitchen.Compliance = "Pass";
        hall.LastServiceDate = new DateTime(2025, 10, 9);
        db.Remarks.AddRange(
            new Remark { Property = glory, Asset = kitchen, Description = "Flow test 10 @ 1.26 L/sec", RaisedOnTaskRef = "T-46682" },
            new Remark { Property = glory, Asset = kitchen, Description = "Flow test/ 96.6 L/m", Active = false, InactiveUtc = Utc(10, 14).AddHours(4) },
            new Remark { Property = glory, Asset = hall, Description = "Closed last month", Active = false, InactiveUtc = Utc(9, 20) },
            new Remark { Property = glory, Asset = hall, Description = "Still open", RaisedOnTaskRef = "T-40000" });
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "work"))!;

        // The property's routine its scope names, due nearest the task
        Assert.Equal("49441", Assert.Single(vm.Visits).Occurrence!.ExternalId);
        // Serviced this month: the kitchen extinguisher, not the hall's (serviced last year)
        Assert.Equal(new DateTime(2026, 10, 14), vm.Performed(kitchen));
        Assert.Null(vm.Performed(hall));
        Assert.Equal((1, 2), (vm.WorkDone, vm.WorkTotal));
        // Identified on the task, and closed off while it was being done
        Assert.Equal(new[] { ("Flow test 10 @ 1.26 L/sec", false), ("Flow test/ 96.6 L/m", true) },
            vm.Remarks.Select(r => (r.Remark.Description!, r.Resolved)).OrderBy(r => r.Item2));
        Assert.Equal(new Dictionary<int, int> { [kitchen.Id] = 1, [hall.Id] = 1 }, vm.OpenRemarks);
    }

    // Two of the property's tasks name the extinguisher routine in their scope of works: October's, and November's
    [Theory]
    // October's still open; November's serviced it on the 5th → November's, not October's
    [InlineData(null, "2026-11-05", "2026-11-05", false, true)]
    // October's done late on 3 Nov, November's still open → October's
    [InlineData("2026-11-03", null, "2026-11-03", true, false)]
    // Serviced in October, before November's started → October's
    [InlineData(null, null, "2026-10-14", true, false)]
    public async Task WorkTab_AnAssetsServiceGoesToTheTaskWhoseScopeCoversItThatDidIt(
        string? octoberPerformed, string? novemberPerformed, string serviced, bool october, bool november)
    {
        using var db = DbContextFactory.Create();
        var (glory, _) = await SeedAsync(db);
        db.RoutineServiceTypes.Add(new RoutineServiceType { Name = Extinguishers });
        ClientTask Task(string title, int month, string? performed) => new()
        {
            ClientId = glory.ClientId, PropertyId = glory.Id, Title = title, ExternalId = title, Ref = "T-" + title,
            DueDateUtc = DateTime.SpecifyKind(new DateTime(2026, month, 28), DateTimeKind.Utc),
            ScopeOfWorks = $"- {Extinguishers} (AS1851-2012 Section 10): {(month == 10 ? "Annual" : "Six-monthly")}",
            PerformedDate = performed == null ? null : DateTime.SpecifyKind(DateTime.Parse(performed), DateTimeKind.Utc)
        };
        var oct = Task("46682", 10, octoberPerformed);
        var nov = Task("46900", 11, novemberPerformed);
        db.ClientTasks.AddRange(oct, nov);
        var kitchen = await db.Assets.SingleAsync(a => a.Name == "FE Kitchen");
        kitchen.LastServiceDate = DateTime.Parse(serviced);
        kitchen.Compliance = "Pass";
        await db.SaveChangesAsync();

        var pages = new TaskPageService(db);
        Assert.Equal(october, (await pages.LoadAsync(oct.Id, "work"))!.Performed(kitchen) != null);
        Assert.Equal(november, (await pages.LoadAsync(nov.Id, "work"))!.Performed(kitchen) != null);
    }

    [Fact]
    public void RoutineType_DropsTheLevel()
    {
        Assert.Equal(Extinguishers, RoutineAssets.RoutineType(Extinguishers + ": Six-monthly"));
        Assert.Equal("Backflow Prevention", RoutineAssets.RoutineType("Backflow Prevention"));
    }
}
