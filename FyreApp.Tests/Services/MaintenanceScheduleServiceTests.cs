using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.MaintenanceSchedules;
using Xunit;

namespace FyreApp.Tests.Services;

public class MaintenanceScheduleServiceTests
{
    private static async Task<(Client client, Site site, MaintenanceInterval interval, MaintenanceSchedule schedule)> SeedDueScheduleAsync(
        FyreApp.Data.AppDbContext db, bool clientActive = true, bool siteActive = true, int monthsOffset = 0)
    {
        var client = new Client { Name = "Acme Corp", Active = clientActive };
        var site = new Site { Name = "Main Site", Client = client, Active = siteActive };
        var interval = new MaintenanceInterval { Name = "Quarterly", Months = 3 };
        var schedule = new MaintenanceSchedule
        {
            TargetType = ScheduleTargetType.Site,
            Site = site,
            StartDate = DateTime.UtcNow.AddMonths(-3),
            MaintenanceInterval = interval,
            NextRunDate = DateTime.UtcNow.Date.AddDays(monthsOffset == 0 ? 1 : 0).AddMonths(monthsOffset),
            IsActive = true
        };

        db.Clients.Add(client);
        db.Sites.Add(site);
        db.MaintenanceIntervals.Add(interval);
        db.MaintenanceSchedules.Add(schedule);
        await db.SaveChangesAsync();

        return (client, site, interval, schedule);
    }

    // -------------------------------------------------------
    // GetDueListAsync
    // -------------------------------------------------------

    [Fact]
    public async Task GetDueListAsync_DefaultFilters_ExcludesInactiveClient()
    {
        using var db = DbContextFactory.Create();
        await SeedDueScheduleAsync(db, clientActive: false);

        var sut = new MaintenanceScheduleService(db);
        var result = await sut.GetDueListAsync(new MaintenanceScheduleFilter());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDueListAsync_DefaultFilters_ExcludesInactiveSite()
    {
        using var db = DbContextFactory.Create();
        await SeedDueScheduleAsync(db, siteActive: false);

        var sut = new MaintenanceScheduleService(db);
        var result = await sut.GetDueListAsync(new MaintenanceScheduleFilter());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDueListAsync_DueThisMonth_IncludesActiveSchedule()
    {
        using var db = DbContextFactory.Create();
        await SeedDueScheduleAsync(db);

        var sut = new MaintenanceScheduleService(db);
        var result = await sut.GetDueListAsync(new MaintenanceScheduleFilter());

        Assert.Single(result);
        Assert.Null(result[0].GeneratedTaskId);
    }

    [Fact]
    public async Task GetDueListAsync_PendingStatus_ExcludesAlreadyGenerated()
    {
        using var db = DbContextFactory.Create();
        var (client, site, _, schedule) = await SeedDueScheduleAsync(db);

        db.ClientTasks.Add(new ClientTask
        {
            Client = client,
            Site = site,
            Title = "Existing",
            Status = ClientTaskStatus.Open,
            DueDateUtc = schedule.NextRunDate,
            MaintenanceScheduleId = schedule.Id
        });
        await db.SaveChangesAsync();

        var sut = new MaintenanceScheduleService(db);
        var pending = await sut.GetDueListAsync(new MaintenanceScheduleFilter { GenerationStatus = ScheduleGenerationStatus.Pending });
        var generated = await sut.GetDueListAsync(new MaintenanceScheduleFilter { GenerationStatus = ScheduleGenerationStatus.Generated });

        Assert.Empty(pending);
        Assert.Single(generated);
        Assert.NotNull(generated[0].GeneratedTaskId);
    }

    // -------------------------------------------------------
    // GenerateTaskAsync
    // -------------------------------------------------------

    [Fact]
    public async Task GenerateTaskAsync_CreatesTask_LinkedToSchedule()
    {
        using var db = DbContextFactory.Create();
        var (_, _, _, schedule) = await SeedDueScheduleAsync(db);

        var sut = new MaintenanceScheduleService(db);
        var task = await sut.GenerateTaskAsync(schedule.Id, "user-1");

        Assert.Equal(schedule.Id, task.MaintenanceScheduleId);
        Assert.Equal(schedule.NextRunDate, task.DueDateUtc);
        Assert.Equal(ClientTaskStatus.Open, task.Status);
        Assert.Single(db.ClientTasks);
    }

    [Fact]
    public async Task GenerateTaskAsync_CalledTwice_IsIdempotent()
    {
        using var db = DbContextFactory.Create();
        var (_, _, _, schedule) = await SeedDueScheduleAsync(db);

        var sut = new MaintenanceScheduleService(db);
        var first = await sut.GenerateTaskAsync(schedule.Id, "user-1");
        var second = await sut.GenerateTaskAsync(schedule.Id, "user-1");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(db.ClientTasks);
    }

    // -------------------------------------------------------
    // CompleteAsync
    // -------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_AdvancesNextRunDateAndLogsHistory()
    {
        using var db = DbContextFactory.Create();
        var (_, _, interval, schedule) = await SeedDueScheduleAsync(db);
        var originalDue = schedule.NextRunDate;

        var sut = new MaintenanceScheduleService(db);
        var status = await sut.CompleteAsync(schedule.Id, "All clear");

        Assert.Equal(ScheduleCompleteStatus.Success, status);

        var updated = db.MaintenanceSchedules.Single(s => s.Id == schedule.Id);
        Assert.Equal(originalDue.Date.AddMonths(interval.Months), updated.NextRunDate);

        var history = db.MaintenanceHistory.Single(h => h.MaintenanceScheduleId == schedule.Id);
        Assert.Equal("All clear", history.Notes);
        Assert.Equal(originalDue, history.DueDateAtCompletion);
    }

    [Fact]
    public async Task CompleteAsync_NotFound_ReturnsNotFoundStatus()
    {
        using var db = DbContextFactory.Create();
        var sut = new MaintenanceScheduleService(db);

        var status = await sut.CompleteAsync(999, null);

        Assert.Equal(ScheduleCompleteStatus.NotFound, status);
    }
}
