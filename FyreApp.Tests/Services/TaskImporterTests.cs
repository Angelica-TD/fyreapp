using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

public class TaskImporterTests
{
    private static readonly string[] Headers =
        { "ID", "Ref", "Created", "Name", "Description", "Scope of works", "Status", "Priority", "Property Ref", "Property Name", "Client", "Category", "Due", "Completed Date" };

    private static Stream Csv(params Dictionary<string, string>[] rows)
    {
        static string Esc(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder(string.Join(",", Headers.Select(Esc)) + "\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", Headers.Select(h => Esc(r.GetValueOrDefault(h, ""))))).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, bool dryRun, params Dictionary<string, string>[] rows) =>
        new UptickImportService(new UptickImporter[] { new TaskImporter(db) })
            .ImportAsync(Csv(rows), "tasks.csv", UptickExportType.Tasks, dryRun);

    // A property with six-monthly and annual schedules, both next due 1 Oct 2026
    private static async Task<(Site Site, MaintenanceSchedule SixMonthly, MaintenanceSchedule Annual)> SeedAsync(AppDbContext db)
    {
        var site = new Site { Name = "Acme HQ", ExternalId = "P-0010", Client = new Client { Name = "Acme" } };
        MaintenanceSchedule Schedule(int months, string name) => new()
        {
            TargetType = ScheduleTargetType.Site, Site = site, IsActive = true,
            MaintenanceInterval = new MaintenanceInterval { Name = name, Months = months },
            StartDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            NextRunDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var six = Schedule(6, "Six-monthly");
        var annual = Schedule(12, "Annual");
        db.MaintenanceSchedules.AddRange(six, annual);
        await db.SaveChangesAsync();
        return (site, six, annual);
    }

    private static Dictionary<string, string> RoutineTask(string status = "READY", string scope =
        "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Six-monthly (2)\n- 04 - Fire Hydrant Systems: Annual") => new()
    {
        ["ID"] = "46616", ["Ref"] = "T-46616", ["Created"] = "2026-09-25 11:55:36",
        ["Name"] = "PM2026/10 Servicing - Portables & Fire Equipment", ["Description"] = "Oct 2026 routine servicing",
        ["Scope of works"] = scope, ["Status"] = status, ["Priority"] = "5",
        ["Property Ref"] = "P-0010", ["Property Name"] = "Acme HQ", ["Client"] = "Acme",
        ["Category"] = "I&T", ["Due"] = "2026-10-31",
        ["Completed Date"] = status == "COMPLETE" ? "2026-10-15 10:00:00" : ""
    };

    [Fact]
    public async Task RoutineTask_LinksToEveryScheduleInItsScope()
    {
        using var db = DbContextFactory.Create();
        var (site, six, annual) = await SeedAsync(db);

        var result = await Import(db, dryRun: false, RoutineTask());

        Assert.Equal(1, result.Created);
        Assert.Empty(result.Skipped);
        var task = await db.ClientTasks.Include(t => t.CoveredSchedules).SingleAsync();
        Assert.Equal(("T-46616", "T-46616", site.Id, site.ClientId), (task.Ref, task.DisplayRef, task.SiteId, task.ClientId));
        Assert.Equal(ClientTaskStatus.Open, task.Status);
        Assert.Equal(new DateTime(2026, 10, 31), task.DueDateUtc);
        Assert.Contains("Scope of works:", task.Description);
        Assert.Equal(new[] { six.Id, annual.Id }.OrderBy(i => i), task.CoveredSchedules.Select(s => s.Id).OrderBy(i => i));
        // Still due: not completed, so schedules stay on 1 Oct
        Assert.All(await db.MaintenanceSchedules.ToListAsync(), s => Assert.Equal(new DateTime(2026, 10, 1), s.NextRunDate));
    }

    [Fact]
    public async Task CompletedRoutineTask_RollsCoveredSchedulesForward()
    {
        using var db = DbContextFactory.Create();
        var (_, six, annual) = await SeedAsync(db);

        var result = await Import(db, dryRun: false, RoutineTask("COMPLETE"));

        Assert.Contains(result.Notes, n => n.Contains("2 schedule occurrences"));
        await db.Entry(six).ReloadAsync();
        await db.Entry(annual).ReloadAsync();
        Assert.Equal(new DateTime(2027, 4, 1), six.NextRunDate);
        Assert.Equal(new DateTime(2027, 10, 1), annual.NextRunDate);
        var history = await db.MaintenanceHistory.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal(("Completed in Uptick (T-46616)", new DateTime(2026, 10, 1)), (h.Notes, h.DueDateAtCompletion)));
    }

    [Fact]
    public async Task DryRun_SavesNothingAndDoesNotMoveSchedules()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        var result = await Import(db, dryRun: true, RoutineTask("COMPLETE"));

        Assert.Equal(1, result.Created);
        Assert.Empty(await db.ClientTasks.ToListAsync());
        Assert.Empty(await db.MaintenanceHistory.ToListAsync());
        Assert.All(await db.MaintenanceSchedules.AsNoTracking().ToListAsync(), s => Assert.Equal(new DateTime(2026, 10, 1), s.NextRunDate));
    }

    [Fact]
    public async Task RepairTaskOnUnknownProperty_ImportedOnPlaceholderWithoutScheduleLinks()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var row = RoutineTask("CANCELLED");
        row["Category"] = "Repair";
        row["Property Ref"] = "P-9999";
        row["Property Name"] = "Old Site";

        var result = await Import(db, dryRun: false, row);

        Assert.Equal(1, result.Created);
        var task = await db.ClientTasks.Include(t => t.CoveredSchedules).Include(t => t.Site).SingleAsync();
        Assert.Equal(("P-9999", true), (task.Site.ExternalId, task.Site.IsPlaceholder));
        Assert.Equal(ClientTaskStatus.Cancelled, task.Status);
        Assert.Empty(task.CoveredSchedules);
    }

    [Fact]
    public async Task Rerun_SkipsAlreadyImportedTasks()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        await Import(db, dryRun: false, RoutineTask());
        var second = await Import(db, dryRun: false, RoutineTask());

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.SkippedExisting);
    }

    [Theory]
    [InlineData("- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual", new[] { 12 })]
    [InlineData("10 - Portable and Wheeled Fire Extinguishers: Six-monthly (1)\n06 - Fire Detection: Monthly (3)", new[] { 6, 1 })]
    [InlineData("Quote to replace failed items as per service report.", new int[0])]
    public void ScopeMonths_ReadsFrequencyPerLine(string scope, int[] expected)
    {
        Assert.Equal(expected, TaskImporter.ScopeMonths(scope));
    }

    [Theory]
    [InlineData("COMPLETE", ClientTaskStatus.Completed)]
    [InlineData("CANCELLED", ClientTaskStatus.Cancelled)]
    [InlineData("PERFORMED", ClientTaskStatus.InProgress)]
    [InlineData("READY", ClientTaskStatus.Open)]
    [InlineData("PENDINGEXT", ClientTaskStatus.Open)]
    public void MapStatus_MapsUptickStatuses(string uptick, ClientTaskStatus expected)
    {
        Assert.Equal(expected, TaskImporter.MapStatus(uptick));
    }
}
