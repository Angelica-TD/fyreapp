using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Routines;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

public class RoutineTests
{
    private const string Header = "ID,Routine,Due Date,Tolerance Period Start Date,Tolerance Period End Date,Status,Completed Date,Property name,Property ref,Client name,Service group";

    private static Stream Routines(params string[] rows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows)));

    private static Task<FyreApp.ViewModels.MaintenanceSchedules.ScheduleImportResultVm> Import(AppDbContext db, Stream csv) =>
        new ScheduleImportService(db).ImportUptickAsync(csv, "routines.csv", dryRun: false);

    [Fact]
    public async Task Import_KeepsEachRowAsAnOccurrenceLinkedToItsSchedule()
    {
        using var db = DbContextFactory.Create();
        db.Sites.Add(new Site { Name = "Magidale", ExternalId = "P-0152", Client = new Client { Name = "Magidale Pty Ltd" } });
        await db.SaveChangesAsync();

        var result = await Import(db, Routines(
            "1,04 - Fire Hydrant Systems (Flow Test): Annual,2026-10-31,2026-08-31,2026-12-31,P,,Magidale,P-0152,Magidale Pty Ltd,Pumps & Tanks",
            "2,04 - Fire Hydrant Systems (Flow Test): Annual,2025-10-31,,,C,2025-10-20,Magidale,P-0152,Magidale Pty Ltd,Pumps & Tanks",
            "3,10 - Portable and Wheeled Fire Extinguishers: Six-monthly,2026-10-01,,,G,,Magidale,P-0152,Magidale Pty Ltd,Servicing"));

        Assert.Equal((3, 0), (result.OccurrencesCreated, result.OccurrencesUpdated));
        var occurrences = await db.RoutineOccurrences.Include(o => o.MaintenanceSchedule!.MaintenanceInterval).OrderBy(o => o.ExternalId).ToListAsync();
        Assert.Equal(new[] { RoutineOccurrenceStatus.Pending, RoutineOccurrenceStatus.Complete, RoutineOccurrenceStatus.Generated }, occurrences.Select(o => o.Status));
        Assert.Equal(new[] { 12, 12, 6 }, occurrences.Select(o => o.MaintenanceSchedule!.MaintenanceInterval.Months));
        Assert.Equal((new DateTime(2026, 8, 31), "Pumps & Tanks"), (occurrences[0].ToleranceStart, occurrences[0].ServiceGroup));
    }

    [Fact]
    public async Task Reimport_UpdatesStatusInsteadOfDuplicating()
    {
        using var db = DbContextFactory.Create();
        db.Sites.Add(new Site { Name = "Magidale", ExternalId = "P-0152", Client = new Client { Name = "Magidale Pty Ltd" } });
        await db.SaveChangesAsync();

        await Import(db, Routines("1,09 - Fire Hose Reels: Annual,2026-10-31,,,P,,Magidale,P-0152,Magidale Pty Ltd,"));
        var second = await Import(db, Routines("1,09 - Fire Hose Reels: Annual,2026-10-31,,,G,,Magidale,P-0152,Magidale Pty Ltd,"));

        Assert.Equal((0, 1), (second.OccurrencesCreated, second.OccurrencesUpdated));
        Assert.Equal(RoutineOccurrenceStatus.Generated, (await db.RoutineOccurrences.SingleAsync()).Status);
    }

    // -------------------------------------------------------
    // Routines page filters (Uptick defaults)
    // -------------------------------------------------------

    private static async Task SeedAsync(AppDbContext db)
    {
        var active = new Client { Name = "Magidale Pty Ltd", Active = true, PrimaryContactName = "Karen Gould" };
        var inactive = new Client { Name = "Gone Pty Ltd", Active = false };
        var site = new Site { Name = "Magidale", ExternalId = "P-0152", Status = "ACTIVE", Client = active };
        var onHold = new Site { Name = "Paused", ExternalId = "P-0200", Status = "ONHOLD", Client = active };
        var goneSite = new Site { Name = "Gone", ExternalId = "P-0300", Status = "ACTIVE", Client = inactive };

        RoutineOccurrence O(Site s, string routine, string due, RoutineOccurrenceStatus status) =>
            new() { Site = s, Routine = routine, DueDate = DateTime.SpecifyKind(DateTime.Parse(due), DateTimeKind.Utc), Status = status };

        db.RoutineOccurrences.AddRange(
            O(site, "04 - Fire Hydrant Systems (Flow Test): Annual", "2026-10-31", RoutineOccurrenceStatus.Pending),     // shown
            O(site, "04 - Fire Hydrant Systems (Landing Valves): Annual", "2026-10-31", RoutineOccurrenceStatus.Pending), // shown
            O(site, "10 - Extinguishers: Six-monthly", "2026-10-01", RoutineOccurrenceStatus.Generated),                 // task raised
            O(site, "09 - Fire Hose Reels: Annual", "2026-11-01", RoutineOccurrenceStatus.Pending),                       // next month
            O(onHold, "09 - Fire Hose Reels: Annual", "2026-10-15", RoutineOccurrenceStatus.Pending),                     // property on hold
            O(goneSite, "09 - Fire Hose Reels: Annual", "2026-10-15", RoutineOccurrenceStatus.Pending));                  // client inactive
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DefaultFilters_MatchUptick()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        var result = await new RoutineService(db).SearchAsync(RoutineFilter.Default(new DateTime(2026, 10, 6)));

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, i => Assert.Equal(("P-0152", "Karen Gould", RoutineOccurrenceStatus.Pending), (i.PropertyRef, i.ClientContact, i.Status)));
        Assert.Equal(4, result.Filter.ActiveCount);
    }

    [Fact]
    public async Task ClearedFilters_ShowEverything_AndSearchNarrows()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var svc = new RoutineService(db);

        var all = await svc.SearchAsync(new RoutineFilter { ClientActive = null, PropertyStatus = new(), Status = new() });
        var hoseReels = await svc.SearchAsync(new RoutineFilter { ClientActive = null, PropertyStatus = new(), Status = new(), Search = "hose reel" });

        Assert.Equal((6, 3), (all.Total, hoseReels.Total));
        Assert.Equal(0, all.Filter.ActiveCount);
    }

    [Fact]
    public void DefaultDueRange_IsTheCurrentMonth()
    {
        var f = RoutineFilter.Default(new DateTime(2026, 2, 14));
        Assert.Equal((new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)), (f.DueFrom!.Value, f.DueTo!.Value));
    }
}
