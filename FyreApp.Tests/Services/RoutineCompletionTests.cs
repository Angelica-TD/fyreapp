using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Routines;
using FyreApp.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// A routine done late moves the property's later routines of that type on, so the next one falls an interval
// after the work was actually done
public class RoutineCompletionTests
{
    private const string Extinguishers = "10 - Portable and Wheeled Fire Extinguishers";

    private static DateTime D(string date) => DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc);

    private static RoutineOccurrence O(Property site, string routine, string due, RoutineOccurrenceStatus status = RoutineOccurrenceStatus.Pending) =>
        new() { Property = site, Routine = routine, DueDate = D(due), ToleranceStart = D(due).AddMonths(-1), ToleranceEnd = D(due).AddMonths(1), Status = status };

    private static ClientTaskService Tasks(AppDbContext db) =>
        new(db, new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object,
            Mock.Of<IMaintenanceScheduleService>());

    private static Property NewSite(string name) => new() { Name = name, Client = new Client { Name = name + " Pty Ltd" } };

    [Fact]
    public async Task SixMonthlyDueAugustDoneNovember_NextMovesFromFebruaryToMay()
    {
        using var db = DbContextFactory.Create();
        var site = NewSite("Magidale");
        var task = new ClientTask { Title = "PM2026/08 Servicing", Client = site.Client, Property = site, DueDateUtc = D("2026-08-31") };
        var august = O(site, Extinguishers + ": Six-monthly", "2026-08-01", RoutineOccurrenceStatus.Generated);
        august.ClientTask = task;
        var february = O(site, Extinguishers + ": Annual", "2027-02-01");
        var nextAugust = O(site, Extinguishers + ": Six-monthly", "2027-08-01");
        var hoseReels = O(site, "09 - Fire Hose Reels: Six-monthly", "2027-02-01");
        var elsewhere = O(NewSite("Calce"), Extinguishers + ": Six-monthly", "2027-02-01");
        db.RoutineOccurrences.AddRange(august, february, nextAugust, hoseReels, elsewhere);
        await db.SaveChangesAsync();

        var result = await RoutineCompletion.CompleteAsync(db, task, new DateTime(2026, 11, 14));
        await db.SaveChangesAsync();

        Assert.Equal((1, 2, 3), (result.Completed, result.Moved, result.MonthsLate));
        Assert.Equal((RoutineOccurrenceStatus.Complete, D("2026-11-14")), (august.Status, august.CompletedDate));
        // The same routine's later ones move 3 months, keeping their levels and tolerance
        Assert.Equal((D("2027-05-01"), D("2027-04-01"), D("2027-06-01")), (february.DueDate, february.ToleranceStart, february.ToleranceEnd));
        Assert.Equal(D("2027-11-01"), nextAugust.DueDate);
        // Other routines, and other properties, don't
        Assert.Equal(D("2027-02-01"), hoseReels.DueDate);
        Assert.Equal(D("2027-02-01"), elsewhere.DueDate);
    }

    [Fact]
    public async Task RoutineServiceSetUpInFyreApp_CountsFromWhenTheLateWorkWasDone()
    {
        using var db = DbContextFactory.Create();
        var site = NewSite("Kamber Apartments");
        var type = new RoutineServiceType { Name = Extinguishers };
        var sixMonthly = new RoutineServiceLevel { RoutineServiceType = type, Name = "Six-monthly", IntervalMonths = 6, ToleranceInterval = 1, ToleranceUnit = "M" };
        db.RoutineServiceLevels.Add(sixMonthly);
        db.Properties.Add(site);
        await db.SaveChangesAsync();
        var setup = new RoutineSetupService(db);
        await setup.AddAsync(site.Id, new() { StartDate = D("2026-01-01"), LastAnnualDate = D("2026-02-01"), Levels = { sixMonthly.Id } }, null);

        // The August routine's task, done in November
        var august = await db.RoutineOccurrences.SingleAsync(o => o.DueDate == D("2026-08-01"));
        var task = new ClientTask { Title = "PM2026/08", ClientId = site.ClientId, PropertyId = site.Id };
        august.ClientTask = task;
        august.Status = RoutineOccurrenceStatus.Generated;
        await db.SaveChangesAsync();
        await RoutineCompletion.CompleteAsync(db, task, new DateTime(2026, 11, 20));
        await db.SaveChangesAsync();

        var service = await db.PropertyRoutineServices.SingleAsync();
        Assert.Equal(D("2026-05-01"), service.LastAnnualDate);
        Assert.Equal(D("2027-05-01"), (await db.RoutineOccurrences.Where(o => o.Status == RoutineOccurrenceStatus.Pending).MinAsync(o => o.DueDate)));

        // Editing it forecasts from the moved date, so the move stays
        await setup.UpdateAsync(service.Id, new() { StartDate = D("2026-01-01"), LastAnnualDate = service.LastAnnualDate, Levels = { sixMonthly.Id } });
        var pending = await db.RoutineOccurrences.Where(o => o.Status == RoutineOccurrenceStatus.Pending).OrderBy(o => o.DueDate).Select(o => o.DueDate).Take(2).ToListAsync();
        Assert.Equal(new[] { D("2026-11-01"), D("2027-05-01") }, pending);
    }

    [Fact]
    public async Task DoneWithinTolerance_NothingMoves()
    {
        using var db = DbContextFactory.Create();
        var site = NewSite("Magidale");
        var task = new ClientTask { Title = "PM2026/08 Servicing", Client = site.Client, Property = site };
        var august = O(site, Extinguishers + ": Six-monthly", "2026-08-01", RoutineOccurrenceStatus.Generated);
        august.ClientTask = task;
        var february = O(site, Extinguishers + ": Six-monthly", "2027-02-01");
        db.RoutineOccurrences.AddRange(august, february);
        await db.SaveChangesAsync();

        var result = await RoutineCompletion.CompleteAsync(db, task, new DateTime(2026, 9, 1)); // tolerance runs to 1 Sep

        Assert.Equal((1, 0), (result.Completed, result.Moved));
        Assert.Equal(D("2027-02-01"), february.DueDate);
    }

    [Fact]
    public async Task ImportedUptickTask_CompletesTheRoutinesItsScopeNames_AndMovesLaterOnes()
    {
        using var db = DbContextFactory.Create();
        var site = NewSite("13 Hutchinson St");
        db.RoutineServiceTypes.AddRange(new RoutineServiceType { Name = Extinguishers }, new RoutineServiceType { Name = "09 - Fire Hose Reels" });
        var task = new ClientTask
        {
            Title = "PM2026/08 Servicing", Client = site.Client, Property = site, ExternalId = "46682", DueDateUtc = D("2026-08-31"),
            UptickData = """{"Scope of works":"- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Six-monthly"}"""
        };
        var august = O(site, Extinguishers + ": Six-monthly", "2026-08-01", RoutineOccurrenceStatus.Generated);
        var hoseReelsRaised = O(site, "09 - Fire Hose Reels: Annual", "2026-08-01", RoutineOccurrenceStatus.Generated); // not in its scope
        var february = O(site, Extinguishers + ": Six-monthly", "2027-02-01");
        db.ClientTasks.Add(task);
        db.RoutineOccurrences.AddRange(august, hoseReelsRaised, february);
        await db.SaveChangesAsync();

        // Marked complete today: late by the months since August
        await Tasks(db).CompleteAsync(task.Id);
        var today = DateTime.UtcNow.ToSydney();
        var late = RoutineCompletion.MonthsLate(D("2026-08-01"), D("2026-09-01"), today);

        Assert.Equal((RoutineOccurrenceStatus.Complete, task.Id), (august.Status, august.ClientTaskId));
        Assert.Equal(RoutineOccurrenceStatus.Generated, hoseReelsRaised.Status);
        Assert.True(late > 0);
        Assert.Equal(D("2027-02-01").AddMonths(late), february.DueDate);
    }

    [Theory]
    [InlineData("2026-08-01", "2026-09-01", "2026-09-01", 0)]  // on the last day of its tolerance
    [InlineData("2026-08-01", "2026-09-01", "2026-09-02", 1)]  // a day after
    [InlineData("2026-08-01", "2026-09-01", "2026-11-14", 3)]
    [InlineData("2026-08-01", null, "2026-08-31", 0)]          // no tolerance: on time within its month
    [InlineData("2026-08-01", null, "2026-10-02", 2)]
    public void MonthsLate(string due, string? toleranceEnd, string done, int expected) =>
        Assert.Equal(expected, RoutineCompletion.MonthsLate(D(due), toleranceEnd == null ? null : D(toleranceEnd), D(done)));
}
