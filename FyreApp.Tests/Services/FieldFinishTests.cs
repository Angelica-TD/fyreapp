using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services;
using FyreApp.Services.Field;
using FyreApp.Services.Lists;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Tasks;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Field;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// Completing a job in the field app: the clock stops, the task closes and its service report is written
public class FieldFinishTests
{
    private sealed record World(AppDbContext Db, UptickUser Me, ClientTask Task, Asset Extinguisher, Asset Hose, Asset Blanket);

    private static async Task<World> SetupAsync()
    {
        var db = DbContextFactory.Create();
        var me = new UptickUser { Name = "Dwayne Page" };
        var site = new Property { Name = "Gemvale Glades", Client = new Client { Name = "Gemvale" } };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "Six-monthly I&T", Ref = "T-1", AssignedTo = me, Status = ClientTaskStatus.InProgress };
        var ext = new Asset { Name = "Extinguisher", Ref = "001", Location = "Lobby", Property = site };
        var hose = new Asset { Name = "Hose reel", Ref = "002", Property = site };
        var blanket = new Asset { Name = "Fire blanket", Ref = "003", Property = site };
        db.AddRange(task, ext, hose, blanket);
        await db.SaveChangesAsync();
        return new World(db, me, task, ext, hose, blanket);
    }

    private static FieldFinishService Service(AppDbContext db) =>
        new(db, new ClientTaskService(db, new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object,
            Mock.Of<IMaintenanceScheduleService>()));

    [Fact]
    public async Task Completing_ClosesTheTask_StopsTheClock_AndWritesTheReport()
    {
        var w = await SetupAsync();
        var now = DateTime.UtcNow;
        w.Db.AddRange(
            new ServiceTask { ClientTaskId = w.Task.Id, AssetId = w.Extinguisher.Id, Routine = "Six-monthly", Result = "Pass", PerformedUtc = now },
            // failed for one of its routines: fails on the report
            new ServiceTask { ClientTaskId = w.Task.Id, AssetId = w.Hose.Id, Routine = "Six-monthly", Result = "Pass", PerformedUtc = now },
            new ServiceTask { ClientTaskId = w.Task.Id, AssetId = w.Hose.Id, Routine = "Annual", Result = "Fail", Notes = "Nozzle cracked", PerformedUtc = now },
            // not tested: not on the report
            new ServiceTask { ClientTaskId = w.Task.Id, AssetId = w.Blanket.Id, Routine = "Six-monthly" },
            new TaskSession { ClientTaskId = w.Task.Id, UptickUserId = w.Me.Id, Type = "Performing Task", IsOnsite = true, StartedUtc = now.AddHours(-2) });
        await w.Db.SaveChangesAsync();

        var done = await Service(w.Db).CompleteAsync(w.Task.Id, w.Me.Id, "  All good bar the hose reel. ", null);

        Assert.Equal(FieldOutcome.Done, done.Result.Outcome);
        var task = await w.Db.ClientTasks.AsNoTracking().SingleAsync(t => t.Id == w.Task.Id);
        Assert.Equal(ClientTaskStatus.Completed, task.Status);
        Assert.False(task.IsActive);
        Assert.NotNull(task.CompletedUtc);
        Assert.NotNull(task.PerformedDate);

        var session = await w.Db.TaskSessions.SingleAsync();
        Assert.NotNull(session.FinishedUtc);
        Assert.Equal(2m, session.Hours);

        var report = await w.Db.Reports.SingleAsync(r => r.Id == done.ReportId);
        Assert.Null(report.UptickId);
        Assert.Equal(w.Task.Id, report.ClientTaskId);
        Assert.Equal(FieldFinishService.ReportType, report.ReportType);
        Assert.Equal("T-1", report.TaskRef);
        Assert.Equal("Dwayne Page", report.Technician);
        Assert.Equal("All good bar the hose reel.", report.GeneralNote);
        Assert.False(report.Compliant);
        Assert.False(report.Published);
        Assert.False(report.Amendment);

        var items = await w.Db.ReportItems.Where(i => i.ReportId == report.Id).OrderBy(i => i.Ref).ToListAsync();
        Assert.Equal(new[] { "001 - Extinguisher", "002 - Hose reel" }, items.Select(i => i.Label));
        Assert.True(items[0].Compliant);
        Assert.Equal("Lobby", items[0].Location);
        Assert.False(items[1].Compliant);
        Assert.Equal("Nozzle cracked", items[1].Notes);
    }

    [Fact]
    public async Task AllPassed_IsCompliant_AndCompletingAgainAfterReopening_IsAnAmendment()
    {
        var w = await SetupAsync();
        w.Db.Add(new ServiceTask { ClientTaskId = w.Task.Id, AssetId = w.Extinguisher.Id, Routine = "Six-monthly", Result = "Pass" });
        await w.Db.SaveChangesAsync();

        var first = await Service(w.Db).CompleteAsync(w.Task.Id, w.Me.Id, null, null);
        Assert.True((await w.Db.Reports.SingleAsync(r => r.Id == first.ReportId)).Compliant);

        // Closed: nothing more to complete
        var again = await Service(w.Db).CompleteAsync(w.Task.Id, w.Me.Id, null, null);
        Assert.Equal(FieldOutcome.Invalid, again.Result.Outcome);
        Assert.Equal(1, await w.Db.Reports.CountAsync());

        // Reopened in the office, completed again
        var task = await w.Db.ClientTasks.SingleAsync(t => t.Id == w.Task.Id);
        (task.Status, task.IsActive, task.CompletedUtc) = (ClientTaskStatus.InProgress, true, null);
        await w.Db.SaveChangesAsync();
        var second = await Service(w.Db).CompleteAsync(w.Task.Id, w.Me.Id, null, null);
        Assert.True((await w.Db.Reports.SingleAsync(r => r.Id == second.ReportId)).Amendment);
    }

    [Fact]
    public async Task TheReportPage_ShowsWhatWasDoneOnTheJob()
    {
        var w = await SetupAsync();
        var remarks = new FieldRemarkService(w.Db, Mock.Of<FyreApp.Services.Files.IFileStore>());
        var found = await remarks.RaiseAsync(w.Task.Id, new RaiseRemarkInput { AssetId = w.Hose.Id, Description = "Nozzle cracked" }, [], w.Me.Id);
        var old = new Remark { PropertyId = w.Task.PropertyId, AssetId = w.Extinguisher.Id, Description = "Missing tag", Active = true };
        w.Db.Add(old);
        w.Db.Add(new TaskSignOff { ClientTaskId = w.Task.Id, SigneeName = "Pat Manager", SignedUtc = DateTime.UtcNow });
        await w.Db.SaveChangesAsync();
        await remarks.ResolveAsync(w.Task.Id, old.Id, "Tagged", [], w.Me.Id);

        var done = await Service(w.Db).CompleteAsync(w.Task.Id, w.Me.Id, null, null);
        var page = await new ReportListService(w.Db).DetailsAsync(done.ReportId!.Value);

        Assert.NotNull(page!.Work);
        Assert.Equal(new[] { found.RemarkId!.Value }, page.Work.Raised.Select(r => r.Id));
        Assert.Equal("002 - Hose reel", page.Work.Raised[0].Asset);
        Assert.Equal(new[] { old.Id }, page.Work.Resolved.Select(r => r.Id));
        Assert.Equal("Tagged", page.Work.Resolved[0].Resolution);
        Assert.Equal("Pat Manager", page.Work.SignOff!.SigneeName);

        // Who raised and resolved them, and on which task
        var events = await w.Db.RemarkEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(new[] { "A", "R" }, events.Select(e => e.Event));
        Assert.All(events, e => Assert.Equal((w.Me.Id, w.Task.Id), (e.UptickUserId!.Value, e.ClientTaskId!.Value)));
    }
}
