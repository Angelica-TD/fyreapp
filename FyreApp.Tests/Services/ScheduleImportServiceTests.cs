using System.Text;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

public class ScheduleImportServiceTests
{
    private const string Header =
        "ID,Created,Updated,Routine,Due Date,Tolerance Period Start Date,Tolerance Period End Date,Status,Completed Date,Property name,Property ref,Property Account Manager,Property branch,Property zone,Client name,Address,Suburb,Service group,Subcontractor,Estimated Duration,Default Technician";

    private static string Row(string routine, string due, string propertyName, string propertyRef, string clientName,
        string status = "P", string completed = "") =>
        $"1,2026-09-17 12:56:05,2026-09-17 12:56:05,{routine},{due},,,{status},{completed},{propertyName},{propertyRef},,,,{clientName},\"1 Test St, \nTown QLD, 4000\",Town,Servicing,,,";

    private static Stream Csv(params string[] rows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows)));

    private static async Task<Site> SeedSiteAsync(FyreApp.Data.AppDbContext db, string client = "Acme", string site = "Acme HQ", string? externalId = null)
    {
        var s = new Site { Name = site, ExternalId = externalId, Client = new Client { Name = client } };
        db.Sites.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    [Theory]
    [InlineData("10 - Portable and Wheeled Fire Extinguishers: Five-yearly", 60)]
    [InlineData("7 - Emergency Lighting: Six-monthly", 6)]
    [InlineData("Hydrants: Yearly", 12)]
    [InlineData("Sprinklers: Monthly", 1)]
    [InlineData("Hose reels: Three-monthly", 3)]
    [InlineData("Pressure test: Twenty-five-yearly", 300)]
    [InlineData("Annual", 12)]
    public void ParseFrequency_KnownLabels(string routine, int expected)
    {
        Assert.Equal(expected, ScheduleImportService.ParseFrequency(routine).Months);
    }

    [Theory]
    [InlineData("Pumps: Weekly")]
    [InlineData("Pumps: Fortnightly")]
    public void ParseFrequency_SubMonthly_IsUnsupported(string routine)
    {
        Assert.Null(ScheduleImportService.ParseFrequency(routine).Months);
    }

    [Fact]
    public async Task Import_MatchesByPropertyRef_CreatesScheduleAndInterval()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, externalId: "P-0001");

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(
            Csv(Row("10 - Portables: Five-yearly", "2036-08-01", "Whatever", "P-0001", "Other name")),
            "export.csv", dryRun: false);

        Assert.Null(result.Error);
        Assert.Equal(1, result.Created);

        var schedule = await db.MaintenanceSchedules.Include(s => s.MaintenanceInterval).SingleAsync();
        Assert.Equal(site.Id, schedule.SiteId);
        Assert.Equal(ScheduleTargetType.Site, schedule.TargetType);
        Assert.Equal(60, schedule.MaintenanceInterval.Months);
        Assert.Equal("Five-yearly", schedule.MaintenanceInterval.Name);
        Assert.Equal(new DateTime(2036, 8, 1), schedule.NextRunDate);
        Assert.Equal(new DateTime(2031, 8, 1), schedule.StartDate);
        Assert.Equal(DateTimeKind.Utc, schedule.NextRunDate.Kind);
    }

    [Fact]
    public async Task Import_MatchesByClientAndPropertyName_LinksExternalId()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, "Acme", "Acme HQ");

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(
            Csv(Row("Portables: Six-monthly", "2026-11-01", "acme hq", "P-0042", "ACME")),
            "export.csv", dryRun: false);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.SitesLinked);
        Assert.Equal("P-0042", (await db.Sites.SingleAsync()).ExternalId);
    }

    [Fact]
    public async Task Import_GroupsOccurrences_UsesEarliestOutstandingDueDate()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db, externalId: "P-1");

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(Csv(
                Row("Portables: Six-monthly", "2026-05-01", "x", "P-1", "x", status: "C", completed: "2026-05-03"),
                Row("Portables: Six-monthly", "2027-05-01", "x", "P-1", "x"),
                Row("Exit lights: Six-monthly", "2026-11-01", "x", "P-1", "x"),
                Row("Hydrants: Yearly", "2027-01-15", "x", "P-1", "x")),
            "export.csv", dryRun: false);

        Assert.Equal(2, result.Created);

        var schedules = await db.MaintenanceSchedules.Include(s => s.MaintenanceInterval).ToListAsync();
        Assert.Equal(new DateTime(2026, 11, 1), schedules.Single(s => s.MaintenanceInterval.Months == 6).NextRunDate);
        Assert.Equal(new DateTime(2027, 1, 15), schedules.Single(s => s.MaintenanceInterval.Months == 12).NextRunDate);
    }

    [Fact]
    public async Task Import_SkipsSiteWithExistingScheduleForSameInterval()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, externalId: "P-1");
        var interval = new MaintenanceInterval { Name = "Annual", Months = 12 };
        db.MaintenanceSchedules.Add(new MaintenanceSchedule
        {
            TargetType = ScheduleTargetType.Site,
            SiteId = site.Id,
            MaintenanceInterval = interval,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NextRunDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(
            Csv(Row("Hydrants: Yearly", "2027-03-01", "x", "P-1", "x")),
            "export.csv", dryRun: false);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedExisting);
        Assert.Equal(1, await db.MaintenanceSchedules.CountAsync());
        Assert.Equal(1, await db.MaintenanceIntervals.CountAsync());
    }

    [Fact]
    public async Task Import_ReusesExistingIntervalWithSameMonths()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db, externalId: "P-1");
        db.MaintenanceIntervals.Add(new MaintenanceInterval { Name = "Annual", Months = 12 });
        await db.SaveChangesAsync();

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(
            Csv(Row("Hydrants: Yearly", "2027-03-01", "x", "P-1", "x")),
            "export.csv", dryRun: false);

        Assert.Empty(result.NewIntervals);
        Assert.Equal("Annual", (await db.MaintenanceSchedules.Include(s => s.MaintenanceInterval).SingleAsync()).MaintenanceInterval.Name);
    }

    [Fact]
    public async Task Import_UnknownProperty_ReportsIssue()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db, externalId: "P-1");

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(
            Csv(Row("Hydrants: Yearly", "2027-03-01", "Nowhere", "P-999", "Nobody")),
            "export.csv", dryRun: false);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedSiteNotFound);
        Assert.Contains(result.Issues, i => i.Type == "SiteNotFound" && i.Key == "P-999");
    }

    [Fact]
    public async Task Import_DryRun_SavesNothing()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db, "Acme", "Acme HQ");

        var sut = new ScheduleImportService(db);
        var result = await sut.ImportUptickAsync(
            Csv(Row("Hydrants: Yearly", "2027-03-01", "Acme HQ", "P-1", "Acme")),
            "export.csv", dryRun: true);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.SitesLinked);
        Assert.Empty(await db.MaintenanceSchedules.ToListAsync());
        Assert.Empty(await db.MaintenanceIntervals.ToListAsync());
        Assert.Null((await db.Sites.SingleAsync()).ExternalId);
    }
}
