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

    // -------------------------------------------------------
    // Generate tasks / download
    // -------------------------------------------------------

    private static async Task<(Site Calce, Site Glory, MaintenanceSchedule Schedule)> SeedForGenerateAsync(AppDbContext db)
    {
        var calce = new Site { Name = "Calce Company", ExternalId = "P-0358", Status = "ACTIVE", Client = new Client { Name = "Calce Company Pty Ltd" } };
        var glory = new Site { Name = "The Glory Well Church", ExternalId = "P-2241", Status = "ACTIVE", Client = new Client { Name = "Sails Ministry" } };
        var schedule = new MaintenanceSchedule
        {
            TargetType = ScheduleTargetType.Site, Site = glory, IsActive = true,
            MaintenanceInterval = new MaintenanceInterval { Name = "Six-monthly", Months = 6 },
            StartDate = DateTime.SpecifyKind(new DateTime(2026, 5, 1), DateTimeKind.Utc),
            NextRunDate = DateTime.SpecifyKind(new DateTime(2026, 11, 30), DateTimeKind.Utc)
        };

        RoutineOccurrence O(Site s, string routine, string group, string due, RoutineOccurrenceStatus status = RoutineOccurrenceStatus.Pending, MaintenanceSchedule? ms = null) =>
            new() { Site = s, Routine = routine, ServiceGroup = group, DueDate = DateTime.SpecifyKind(DateTime.Parse(due), DateTimeKind.Utc), Status = status, MaintenanceSchedule = ms };

        db.RoutineOccurrences.AddRange(
            O(calce, "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", "Servicing - Portables & Fire Equipment", "2026-11-30"),
            O(glory, "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", "Servicing - Portables & Fire Equipment", "2026-11-30", ms: schedule),
            O(glory, "11 - Fire Blankets: Six-monthly", "Servicing - Portables & Fire Equipment", "2026-11-30", ms: schedule),
            O(glory, "06 - Fire Detection (Fire Panels): Monthly", "Fire Detection (FIP, Exits) System Servicing", "2026-11-15"),
            O(glory, "09 - Fire Hose Reels: Annual", "Servicing - Portables & Fire Equipment", "2026-11-30", RoutineOccurrenceStatus.Generated));
        await db.SaveChangesAsync();
        return (calce, glory, schedule);
    }

    private static RoutineFilter AnyFilter => new() { ClientActive = null, PropertyStatus = new(), Status = new() };

    [Fact]
    public async Task GenerateTasks_OneTaskPerPropertyServiceGroupAndMonth()
    {
        using var db = DbContextFactory.Create();
        var (_, glory, schedule) = await SeedForGenerateAsync(db);
        var all = await db.RoutineOccurrences.Select(o => o.Id).ToListAsync();

        var result = await new RoutineService(db).GenerateTasksAsync(all, allMatching: false, AnyFilter, "user-1");

        // Calce portables; Glory portables (extinguishers + blankets); Glory fire detection. Hose reels already had a task.
        Assert.Equal((3, 4, 1), (result.TasksCreated, result.RoutinesUsed, result.RoutinesSkipped));

        var tasks = await db.ClientTasks.Include(t => t.CoveredSchedules).ToListAsync();
        var gloryPortables = tasks.Single(t => t.SiteId == glory.Id && t.Title == "PM2026/11 Servicing - Portables & Fire Equipment");
        Assert.Equal(("I&T", ClientTaskStatus.Open, new DateTime(2026, 11, 30), "user-1"),
            (gloryPortables.Category, gloryPortables.Status, gloryPortables.DueDateUtc, gloryPortables.CreatedByUserId));
        Assert.Contains("- 11 - Fire Blankets: Six-monthly", gloryPortables.Description);
        Assert.Equal(schedule.Id, Assert.Single(gloryPortables.CoveredSchedules).Id);

        var occurrences = await db.RoutineOccurrences.ToListAsync();
        Assert.All(occurrences.Where(o => o.Routine != "09 - Fire Hose Reels: Annual"), o =>
            Assert.Equal((RoutineOccurrenceStatus.Generated, true), (o.Status, o.ClientTaskId != null)));
        Assert.Null(occurrences.Single(o => o.Routine == "09 - Fire Hose Reels: Annual").ClientTaskId);
    }

    [Fact]
    public async Task GenerateTasks_AllMatching_UsesTheFilterNotTheTickedIds()
    {
        using var db = DbContextFactory.Create();
        var (calce, _, _) = await SeedForGenerateAsync(db);

        var filter = AnyFilter;
        filter.Search = "calce";
        var result = await new RoutineService(db).GenerateTasksAsync(Array.Empty<int>(), allMatching: true, filter, null);

        Assert.Equal((1, 1, 0), (result.TasksCreated, result.RoutinesUsed, result.RoutinesSkipped));
        Assert.Equal(calce.Id, (await db.ClientTasks.SingleAsync()).SiteId);
    }

    [Fact]
    public async Task Download_HasEveryMatchingRoutine()
    {
        using var db = DbContextFactory.Create();
        await SeedForGenerateAsync(db);

        var filter = AnyFilter;
        filter.Status = new() { RoutineOccurrenceStatus.Pending };
        var csv = Encoding.UTF8.GetString(await new RoutineService(db).DownloadCsvAsync(filter)).TrimStart('﻿');
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Routine,Property ref,Property,", lines[0]);
        Assert.Equal(5, lines.Length); // header + 4 pending
        Assert.Contains(lines, l => l.Contains("P-2241") && l.Contains("2026-11-15") && l.Contains("Pending"));
    }
}
