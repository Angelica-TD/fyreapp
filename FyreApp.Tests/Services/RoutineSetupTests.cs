using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FyreApp.Services.Routines.RoutineSetupService;

namespace FyreApp.Tests.Services;

// Property → Routines → Add routine services (Uptick's modal), and forecasting the routines it adds
public class RoutineSetupTests
{
    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    // "14 - Emergency planning in facilities", as Uptick's levels export has it
    private static readonly ForecastLevel SixMonthly = new(1, 6, null, 1, false, 1, "M");
    private static readonly ForecastLevel Annual = new(2, 12, null, 2, true, 2, "M");

    [Fact]
    public void Forecast_CountsFromTheLastAnnual_AndTheAnnualSupersedesTheSixMonthly()
    {
        var due = Forecast(new[] { SixMonthly, Annual }, start: Day(2026, 4, 1), lastAnnual: Day(2026, 3, 15), end: null, today: Day(2026, 10, 14));

        Assert.Equal(new[]
        {
            (Day(2026, 9, 1), 1), (Day(2027, 3, 1), 2), (Day(2027, 9, 1), 1), (Day(2028, 3, 1), 2)
        }, due.Take(4).Select(o => (o.DueDate, o.Level.Id)));
        // Never both in the annual's month
        Assert.DoesNotContain(due, o => o.Level.Id == 1 && o.DueDate.Month == 3);
        // Tolerance either side of the due date
        Assert.Equal((Day(2026, 8, 1), Day(2026, 10, 1)), (due[0].ToleranceStart, due[0].ToleranceEnd));
        // Forecast ten years ahead
        Assert.Equal(Day(2036, 9, 1), due.Max(o => o.DueDate));
    }

    [Fact]
    public void Forecast_StopsAtTheEndDate_AndSkipsWhatsBeforeTheStart()
    {
        var due = Forecast(new[] { SixMonthly }, start: Day(2027, 1, 1), lastAnnual: Day(2026, 3, 1), end: Day(2028, 6, 30), today: Day(2026, 10, 14));

        Assert.Equal(new[] { Day(2027, 3, 1), Day(2027, 9, 1), Day(2028, 3, 1) }, due.Select(o => o.DueDate));
    }

    [Fact]
    public void Forecast_FirstDueAfterTheOffset_AndNoToleranceWhenTheLevelHasNone()
    {
        var fiveYearly = new ForecastLevel(3, 60, 60, 4, true, null, null);
        var due = Forecast(new[] { fiveYearly }, start: Day(2026, 1, 1), lastAnnual: Day(2026, 10, 1), end: null, today: Day(2026, 10, 14));

        Assert.Equal(new[] { Day(2031, 10, 1), Day(2036, 10, 1) }, due.Select(o => o.DueDate));
        Assert.Null(due[0].ToleranceStart);
    }

    private static async Task<(FyreApp.Data.AppDbContext Db, Property Property, RoutineServiceType Type)> SeedAsync()
    {
        var db = DbContextFactory.Create();
        var site = new Property { Name = "Gemvale Glades", Client = new Client { Name = "Gemvale" } };
        var type = new RoutineServiceType { Name = "14 - Emergency planning in facilities", Standard = "AS1851-2012", StandardReference = "AS1851-2012 Section 14", ServiceGroup = "Emergency Planning" };
        type.Levels.Add(new RoutineServiceLevel { Name = "Six-monthly", IntervalMonths = 6, ServiceRank = 1, DefaultEnabled = true, ToleranceInterval = 1, ToleranceUnit = "M" });
        type.Levels.Add(new RoutineServiceLevel { Name = "Annual", IntervalMonths = 12, ServiceRank = 2, SupersedesLowerRank = true, DefaultEnabled = true, ToleranceInterval = 2, ToleranceUnit = "M" });
        type.Levels.Add(new RoutineServiceLevel { Name = "Retired", IntervalMonths = 3, Active = false });
        var plans = new AssetType { Name = "Evacuation Diagram", UptickData = """{"Applicable Routines":"14 - Emergency planning in facilities\n15 - Emergency Escape Lighting and Exit Signs"}""" };
        db.AddRange(site, type, new Contractor { Name = "Rays Advantage Electrical" });
        db.Assets.AddRange(
            new Asset { Name = "Diagram level 1", Property = site, AssetTypes = { plans } },
            new Asset { Name = "Diagram level 2", Property = site, AssetTypes = { plans }, IsActive = false });
        await db.SaveChangesAsync();
        return (db, site, type);
    }

    [Fact]
    public async Task Form_ListsActiveLevels_ApplicableAssets_AndRoutinesAlreadyThere()
    {
        var (db, site, type) = await SeedAsync();
        using var _ = db;
        var svc = new RoutineSetupService(db);

        var form = await svc.FormAsync(site.Id);
        var option = Assert.Single(form.Types);
        Assert.Equal(("AS1851-2012 Section 14", 1, 0), (option.Reference, option.ApplicableAssets, option.ExistingRoutines));
        Assert.Equal(new[] { "Six-monthly", "Annual" }, option.Levels.Select(l => l.Name));
        Assert.Equal(new[] { "AS1851-2012" }, form.Standards);
        Assert.Equal(new[] { "Rays Advantage Electrical" }, form.Contractors);

        await svc.AddAsync(site.Id, new AddRoutineServicesInput
        {
            StartDate = Day(2026, 4, 1), LastAnnualDate = Day(2026, 3, 15), Levels = type.Levels.Where(l => l.Active).Select(l => l.Id).ToList()
        }, "admin");
        Assert.Equal(2, Assert.Single((await svc.FormAsync(site.Id)).Types).ExistingRoutines);
    }

    [Fact]
    public async Task Add_CreatesTheRoutineService_AndForecastsItsRoutines()
    {
        var (db, site, type) = await SeedAsync();
        using var _ = db;
        var annual = type.Levels.Single(l => l.Name == "Annual");

        var result = await new RoutineSetupService(db).AddAsync(site.Id, new AddRoutineServicesInput
        {
            StartDate = new DateTime(2026, 4, 1), LastAnnualDate = new DateTime(2026, 3, 15), EndDate = new DateTime(2028, 12, 31),
            Subcontractor = "Rays Advantage Electrical", Levels = new() { annual.Id }
        }, "admin");

        Assert.Equal((1, 2, (string?)null, Day(2027, 3, 1)), (result.Services, result.Occurrences, result.Error, result.FirstDue));
        var service = await db.PropertyRoutineServices.Include(s => s.Levels).SingleAsync();
        Assert.Equal((type.Id, "Annual", "admin"), (service.RoutineServiceTypeId, Assert.Single(service.Levels).Name, service.CreatedBy));
        var routines = await db.RoutineOccurrences.OrderBy(o => o.DueDate).ToListAsync();
        Assert.Equal(new[] { Day(2027, 3, 1), Day(2028, 3, 1) }, routines.Select(o => o.DueDate));
        Assert.All(routines, o =>
        {
            Assert.Equal("14 - Emergency planning in facilities: Annual", o.Routine);
            Assert.Equal((RoutineOccurrenceStatus.Pending, "Emergency Planning", "Rays Advantage Electrical", service.Id),
                (o.Status, o.ServiceGroup, o.Subcontractor, o.PropertyRoutineServiceId));
        });

        // They show on the property's Routines tab like imported ones
        var grid = await new RoutineService(db).ForPropertyAsync(site.Id, 2027);
        Assert.Equal("14 - Emergency planning in facilities", Assert.Single(Assert.Single(grid.Groups).Rows).Routine);
    }

    // A six-monthly + annual service from Oct 2026, whose first routine (Mar 2027) already has a task
    private static async Task<(FyreApp.Data.AppDbContext Db, Property Property, RoutineServiceType Type, PropertyRoutineService Service)> WithServiceAsync()
    {
        var (db, site, type) = await SeedAsync();
        var svc = new RoutineSetupService(db);
        await svc.AddAsync(site.Id, new AddRoutineServicesInput
        {
            StartDate = new DateTime(2026, 10, 1), LastAnnualDate = new DateTime(2026, 3, 15), EndDate = new DateTime(2028, 12, 31),
            Levels = type.Levels.Where(l => l.Active).Select(l => l.Id).ToList()
        }, "admin");
        var first = await db.RoutineOccurrences.OrderBy(o => o.DueDate).FirstAsync();
        first.Status = RoutineOccurrenceStatus.Generated;
        first.ClientTask = new ClientTask { Property = site, ClientId = site.ClientId, Title = "PM2027/03 Emergency Planning" };
        await db.SaveChangesAsync();
        return (db, site, type, await db.PropertyRoutineServices.SingleAsync());
    }

    [Fact]
    public async Task Edit_ForecastsAgain_AndKeepsRoutinesThatHaveATask()
    {
        var (db, site, type, service) = await WithServiceAsync();
        using var _ = db;
        // Before: Mar 2027 annual (task raised), Sep 2027 six-monthly, Mar 2028 annual, Sep 2028 six-monthly
        Assert.Equal(4, await db.RoutineOccurrences.CountAsync());
        var annual = type.Levels.Single(l => l.Name == "Annual");

        var result = await new RoutineSetupService(db).UpdateAsync(service.Id, new EditRoutineServiceInput
        {
            StartDate = new DateTime(2026, 10, 1), LastAnnualDate = new DateTime(2026, 3, 15), EndDate = new DateTime(2029, 12, 31),
            Subcontractor = "Rays Advantage Electrical", Levels = new() { annual.Id }
        });

        Assert.Equal((true, site.Id, (string?)null, 3, 2, 1), (result.Found, result.PropertyId, result.Error, result.Removed, result.Added, result.Kept));
        var routines = await db.RoutineOccurrences.OrderBy(o => o.DueDate).ToListAsync();
        // The Mar 2027 one with its task is kept (not forecast twice); the rest are annuals to the new end date
        Assert.Equal(new[] { Day(2027, 3, 1), Day(2028, 3, 1), Day(2029, 3, 1) }, routines.Select(o => o.DueDate));
        Assert.NotNull(routines[0].ClientTaskId);
        Assert.Equal(new[] { null, "Rays Advantage Electrical", "Rays Advantage Electrical" }, routines.Select(o => o.Subcontractor));
        var saved = await db.PropertyRoutineServices.Include(s => s.Levels).SingleAsync();
        Assert.Equal(("Annual", Day(2029, 12, 31)), (Assert.Single(saved.Levels).Name, saved.EndDate));
    }

    [Fact]
    public async Task Edit_NeedsALevel_AndAnUnknownServiceIsNotFound()
    {
        var (db, _, _, service) = await WithServiceAsync();
        using var __ = db;
        var svc = new RoutineSetupService(db);

        var noLevels = await svc.UpdateAsync(service.Id, new EditRoutineServiceInput { StartDate = new DateTime(2026, 10, 1), LastAnnualDate = new DateTime(2026, 3, 1) });
        Assert.Equal("Tick at least one level (or remove the routine service).", noLevels.Error);
        Assert.Equal(4, await db.RoutineOccurrences.CountAsync());
        Assert.False((await svc.UpdateAsync(999, new EditRoutineServiceInput())).Found);
        Assert.Null(await svc.EditFormAsync(999));
    }

    [Fact]
    public async Task EditForm_HasTheServicesDatesAndLevels()
    {
        var (db, _, _, service) = await WithServiceAsync();
        using var _ = db;

        var form = (await new RoutineSetupService(db).EditFormAsync(service.Id))!;

        Assert.Equal("14 - Emergency planning in facilities", form.RoutineType);
        Assert.Equal(new[] { "Six-monthly", "Annual" }, form.Levels.Select(l => l.Name));
        Assert.Equal(2, form.Input.Levels.Count);
        Assert.Equal((Day(2026, 10, 1), Day(2026, 3, 15)), (form.Input.StartDate, form.Input.LastAnnualDate));
    }

    [Fact]
    public async Task Remove_DeletesPendingRoutines_AndKeepsOnesWithATask()
    {
        var (db, site, _, service) = await WithServiceAsync();
        using var _ = db;
        var svc = new RoutineSetupService(db);
        Assert.Equal(3, Assert.Single(await svc.ListAsync(site.Id)).PendingRoutines);

        var result = await svc.RemoveAsync(service.Id);

        Assert.Equal((true, 3, 1), (result.Found, result.Removed, result.Kept));
        Assert.Equal(0, await db.PropertyRoutineServices.CountAsync());
        var kept = await db.RoutineOccurrences.SingleAsync();
        Assert.Equal((Day(2027, 3, 1), (int?)null), (kept.DueDate, kept.PropertyRoutineServiceId));
        Assert.Empty(await svc.ListAsync(site.Id));
        Assert.False((await svc.RemoveAsync(service.Id)).Found);
    }

    [Theory]
    [InlineData(false, true, "Start date and last annual date are required.")]
    [InlineData(true, false, "Tick at least one routine type and level.")]
    public async Task Add_NeedsDatesAndALevel(bool withDates, bool withLevel, string error)
    {
        var (db, site, type) = await SeedAsync();
        using var _ = db;

        var result = await new RoutineSetupService(db).AddAsync(site.Id, new AddRoutineServicesInput
        {
            StartDate = withDates ? new DateTime(2026, 4, 1) : null,
            LastAnnualDate = withDates ? new DateTime(2026, 3, 1) : null,
            Levels = withLevel ? new() { type.Levels.First().Id } : new()
        }, "admin");

        Assert.Equal(error, result.Error);
        Assert.Equal(0, await db.PropertyRoutineServices.CountAsync());
    }
}
