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
    [Fact]
    public async Task PropertyRoutines_GroupShowsTechnicianAndEstimatedHours()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Camajay", Client = new Client { Name = "Camajay" } };
        Routine O(string due, string? tech, int? minutes) => new()
        {
            Property = site, Name = "10 - Extinguishers: Six-monthly", ServiceGroup = "Servicing",
            DueDate = DateTime.SpecifyKind(DateTime.Parse(due), DateTimeKind.Utc), DefaultTechnician = tech, EstimatedMinutes = minutes
        };
        db.Routines.AddRange(O("2026-04-30", "Neil", 30), O("2026-10-31", "Neil", 60), O("2026-06-30", null, null));
        await db.SaveChangesAsync();

        var group = Assert.Single((await new RoutineListService(db).ForPropertyAsync(site.Id, 2026)).Groups);

        Assert.Equal(("Neil", 90), (group.Technician, group.EstimatedMinutes));
    }

    // -------------------------------------------------------
    // Routines page filters (Uptick defaults)
    // -------------------------------------------------------

    private static async Task SeedAsync(AppDbContext db)
    {
        var active = new Client { Name = "Magidale Pty Ltd", Active = true, PrimaryContactName = "Karen Gould" };
        var inactive = new Client { Name = "Gone Pty Ltd", Active = false };
        var site = new Property { Name = "Magidale", ExternalId = "P-0152", Status = "ACTIVE", Client = active };
        var onHold = new Property { Name = "Paused", ExternalId = "P-0200", Status = "ONHOLD", Client = active };
        var goneSite = new Property { Name = "Gone", ExternalId = "P-0300", Status = "ACTIVE", Client = inactive };

        Routine O(Property s, string routine, string due, RoutineStatus status) =>
            new() { Property = s, Name = routine, DueDate = DateTime.SpecifyKind(DateTime.Parse(due), DateTimeKind.Utc), Status = status };

        db.Routines.AddRange(
            O(site, "04 - Fire Hydrant Systems (Flow Test): Annual", "2026-10-31", RoutineStatus.Pending),     // shown
            O(site, "04 - Fire Hydrant Systems (Landing Valves): Annual", "2026-10-31", RoutineStatus.Pending), // shown
            O(site, "10 - Extinguishers: Six-monthly", "2026-10-01", RoutineStatus.Generated),                 // task raised
            O(site, "09 - Fire Hose Reels: Annual", "2026-11-01", RoutineStatus.Pending),                       // next month
            O(onHold, "09 - Fire Hose Reels: Annual", "2026-10-15", RoutineStatus.Pending),                     // property on hold
            O(goneSite, "09 - Fire Hose Reels: Annual", "2026-10-15", RoutineStatus.Pending));                  // client inactive
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DefaultFilters_MatchUptick()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        var result = await new RoutineListService(db).SearchAsync(RoutineFilter.Default(new DateTime(2026, 10, 6)));

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, i => Assert.Equal(("P-0152", "Karen Gould", RoutineStatus.Pending), (i.PropertyRef, i.ClientContact, i.Status)));
        Assert.Equal(4, result.Filter.ActiveCount);
    }

    [Fact]
    public async Task ClearedFilters_ShowEverything_AndSearchNarrows()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var svc = new RoutineListService(db);

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

    private static async Task<(Property Calce, Property Glory, MaintenanceSchedule Schedule)> SeedForGenerateAsync(AppDbContext db)
    {
        var calce = new Property { Name = "Calce Company", ExternalId = "P-0358", Status = "ACTIVE", Client = new Client { Name = "Calce Company Pty Ltd" } };
        var glory = new Property { Name = "The Glory Well Church", ExternalId = "P-2241", Status = "ACTIVE", Client = new Client { Name = "Sails Ministry" } };
        var schedule = new MaintenanceSchedule
        {
            TargetType = ScheduleTargetType.Property, Property = glory, IsActive = true,
            MaintenanceInterval = new MaintenanceInterval { Name = "Six-monthly", Months = 6 },
            StartDate = DateTime.SpecifyKind(new DateTime(2026, 5, 1), DateTimeKind.Utc),
            NextRunDate = DateTime.SpecifyKind(new DateTime(2026, 11, 30), DateTimeKind.Utc)
        };

        Routine O(Property s, string routine, string group, string due, RoutineStatus status = RoutineStatus.Pending, MaintenanceSchedule? ms = null) =>
            new() { Property = s, Name = routine, ServiceGroup = group, DueDate = DateTime.SpecifyKind(DateTime.Parse(due), DateTimeKind.Utc), Status = status, MaintenanceSchedule = ms };

        db.Routines.AddRange(
            O(calce, "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", "Servicing - Portables & Fire Equipment", "2026-11-30"),
            O(glory, "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", "Servicing - Portables & Fire Equipment", "2026-11-30", ms: schedule),
            O(glory, "11 - Fire Blankets: Six-monthly", "Servicing - Portables & Fire Equipment", "2026-11-30", ms: schedule),
            O(glory, "06 - Fire Detection (Fire Panels): Monthly", "Fire Detection (FIP, Exits) System Servicing", "2026-11-15"),
            O(glory, "09 - Fire Hose Reels: Annual", "Servicing - Portables & Fire Equipment", "2026-11-30", RoutineStatus.Generated));
        await db.SaveChangesAsync();
        return (calce, glory, schedule);
    }

    private static RoutineFilter AnyFilter => new() { ClientActive = null, PropertyStatus = new(), Status = new() };

    [Fact]
    public async Task GenerateTasks_OneTaskPerPropertyServiceGroupAndMonth()
    {
        using var db = DbContextFactory.Create();
        var (_, glory, schedule) = await SeedForGenerateAsync(db);
        var all = await db.Routines.Select(o => o.Id).ToListAsync();

        var result = await new RoutineListService(db).GenerateTasksAsync(all, allMatching: false, AnyFilter, "user-1");

        // Calce portables; Glory portables (extinguishers + blankets); Glory fire detection. Hose reels already had a task.
        Assert.Equal((3, 4, 1), (result.TasksCreated, result.RoutinesUsed, result.RoutinesSkipped));

        var tasks = await db.ClientTasks.Include(t => t.CoveredSchedules).ToListAsync();
        var gloryPortables = tasks.Single(t => t.PropertyId == glory.Id && t.Title == "PM2026/11 Servicing - Portables & Fire Equipment");
        Assert.Equal(("I&T", ClientTaskStatus.Open, new DateTime(2026, 11, 30), "user-1"),
            (gloryPortables.Category, gloryPortables.Status, gloryPortables.DueDateUtc, gloryPortables.CreatedByUserId));
        Assert.Contains("- 11 - Fire Blankets: Six-monthly", gloryPortables.Description);
        Assert.Equal(schedule.Id, Assert.Single(gloryPortables.CoveredSchedules).Id);

        var occurrences = await db.Routines.ToListAsync();
        Assert.All(occurrences.Where(o => o.Name != "09 - Fire Hose Reels: Annual"), o =>
            Assert.Equal((RoutineStatus.Generated, true), (o.Status, o.ClientTaskId != null)));
        Assert.Null(occurrences.Single(o => o.Name == "09 - Fire Hose Reels: Annual").ClientTaskId);
    }

    [Fact]
    public async Task GenerateTasks_AllMatching_UsesTheFilterNotTheTickedIds()
    {
        using var db = DbContextFactory.Create();
        var (calce, _, _) = await SeedForGenerateAsync(db);

        var filter = AnyFilter;
        filter.Search = "calce";
        var result = await new RoutineListService(db).GenerateTasksAsync(Array.Empty<int>(), allMatching: true, filter, null);

        Assert.Equal((1, 1, 0), (result.TasksCreated, result.RoutinesUsed, result.RoutinesSkipped));
        Assert.Equal(calce.Id, (await db.ClientTasks.SingleAsync()).PropertyId);
    }

    [Fact]
    public async Task Download_HasEveryMatchingRoutine()
    {
        using var db = DbContextFactory.Create();
        await SeedForGenerateAsync(db);

        var filter = AnyFilter;
        filter.Status = new() { RoutineStatus.Pending };
        var csv = Encoding.UTF8.GetString(await new RoutineListService(db).DownloadCsvAsync(filter)).TrimStart('﻿');
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Routine,Property ref,Property,", lines[0]);
        Assert.Equal(5, lines.Length); // header + 4 pending
        Assert.Contains(lines, l => l.Contains("P-2241") && l.Contains("2026-11-15") && l.Contains("Pending"));
    }
}
