using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// The technicians' field app: their day, their clock on a task, and the assets they pass or fail
public class FieldServiceTests
{
    private sealed record World(AppDbContext Db, UptickUser Me, UptickUser Other, ClientTask Task, ClientTask Elsewhere);

    private static async Task<World> SetupAsync()
    {
        var db = DbContextFactory.Create();
        var me = new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD" };
        var other = new UptickUser { Name = "Sam Tech", LicenseType = "FIELD" };
        var site = new Property { Name = "Gemvale Glades", AddressDisplay = "1 Gemvale Rd, Mermaid Waters", Client = new Client { Name = "Gemvale" } };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "Six-monthly I&T", Ref = "T-1", AssignedTo = me };
        var elsewhere = new ClientTask { Property = site, Client = site.Client, Title = "Repair", Ref = "T-2", AssignedTo = me };
        db.AddRange(task, elsewhere, other);
        await db.SaveChangesAsync();
        return new World(db, me, other, task, elsewhere);
    }

    // 9am on 10 Mar 2026 in Sydney (AEDT, UTC+11) is 10pm on the 9th in UTC
    private static readonly DateTime Day = new(2026, 3, 10);
    private static readonly DateTime NineAmUtc = new(2026, 3, 9, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheDay_ListsVisitsBookedForTheTechnicianThatSydneyDay_ThenTasksScheduledForIt()
    {
        var w = await SetupAsync();
        var third = new ClientTask { Property = w.Task.Property, Client = w.Task.Client, Title = "Callout", Ref = "T-3", AssignedTo = w.Me,
            ScheduledDate = DateTime.SpecifyKind(Day, DateTimeKind.Utc) };
        var done = new ClientTask { Property = w.Task.Property, Client = w.Task.Client, Title = "Done", Ref = "T-4", AssignedTo = w.Me, Status = ClientTaskStatus.Completed, IsActive = false };
        var others = new ClientTask { Property = w.Task.Property, Client = w.Task.Client, Title = "Theirs", Ref = "T-5", AssignedTo = w.Other };
        w.Db.AddRange(done, others);
        w.Db.AddRange(
            third,
            new Appointment { ClientTask = w.Task, StartUtc = NineAmUtc, EndUtc = NineAmUtc.AddHours(2), Status = "scheduled", Technicians = [w.Me] },
            // the day before in Sydney, cancelled, and someone else's
            new Appointment { ClientTask = w.Elsewhere, StartUtc = NineAmUtc.AddDays(-1), Status = "scheduled", Technicians = [w.Me] },
            new Appointment { ClientTask = w.Elsewhere, StartUtc = NineAmUtc, Status = "cancelled", Technicians = [w.Me] },
            new Appointment { ClientTask = w.Elsewhere, StartUtc = NineAmUtc, Status = "scheduled", Technicians = [w.Other] });
        await w.Db.SaveChangesAsync();

        var day = await new FieldService(w.Db).DayAsync(w.Me.Id, Day);

        Assert.Equal(new[] { "T-1", "T-3" }, day.Visits.Select(v => v.TaskRef));
        Assert.Equal(new DateTime(2026, 3, 10, 9, 0, 0), day.Visits[0].StartLocal);
        Assert.Null(day.Visits[1].StartLocal);
        Assert.Equal("Dwayne Page", day.Technician);

        // Their other open tasks: not those on the day, closed ones or someone else's
        Assert.Equal(new[] { "T-2" }, day.Assigned.Select(v => v.TaskRef));
    }

    [Fact]
    public async Task StartingWork_FinishesTheClockElsewhere_AndPutsTheTaskInProgress()
    {
        var w = await SetupAsync();
        var field = new FieldService(w.Db);

        await field.StartAsync(w.Elsewhere.Id, w.Me.Id, "Travelling", null);
        var started = await field.StartAsync(w.Task.Id, w.Me.Id, "Performing Task", new Coords(-28.0, 153.4));

        Assert.Equal(FieldOutcome.Done, started.Outcome);
        var sessions = await w.Db.TaskSessions.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.NotNull(sessions[0].FinishedUtc);
        Assert.Equal((w.Task.Id, "Performing Task", true, -28.0), (sessions[1].ClientTaskId, sessions[1].Type, sessions[1].IsOnsite, sessions[1].StartedLatitude!.Value));
        Assert.Null(sessions[1].FinishedUtc);
        Assert.Equal(ClientTaskStatus.InProgress, w.Task.Status);

        var vm = (await field.TaskAsync(w.Task.Id, w.Me.Id))!;
        Assert.Equal(sessions[1].Id, vm.Running!.Id);
    }

    [Fact]
    public async Task Finishing_RecordsTheHoursAndNote()
    {
        var w = await SetupAsync();
        w.Db.TaskSessions.Add(new TaskSession { ClientTask = w.Task, UptickUserId = w.Me.Id, Type = "Performing Task", IsOnsite = true, StartedUtc = DateTime.UtcNow.AddMinutes(-90) });
        await w.Db.SaveChangesAsync();
        var field = new FieldService(w.Db);

        var notRunning = await field.FinishAsync(w.Elsewhere.Id, w.Me.Id, null, null);
        await field.FinishAsync(w.Task.Id, w.Me.Id, null, "Tested all extinguishers");

        Assert.Equal(FieldOutcome.Invalid, notRunning.Outcome);
        var s = await w.Db.TaskSessions.SingleAsync();
        Assert.Equal(1.5m, s.Hours);
        Assert.Equal("Tested all extinguishers", s.Description);
    }

    [Fact]
    public async Task UptickSessions_AreLeftForUptickToFinish()
    {
        var w = await SetupAsync();
        w.Db.TaskSessions.Add(new TaskSession { UptickId = 99, ClientTask = w.Elsewhere, UptickUserId = w.Me.Id, Type = "Travelling", StartedUtc = DateTime.UtcNow.AddHours(-1) });
        await w.Db.SaveChangesAsync();

        await new FieldService(w.Db).StartAsync(w.Task.Id, w.Me.Id, "Performing Task", null);

        Assert.Null((await w.Db.TaskSessions.SingleAsync(s => s.UptickId == 99)).FinishedUtc);
    }

    [Fact]
    public async Task AnAssetsResult_IsRecordedWithWhoAndWhen_AndCanBeCleared()
    {
        var w = await SetupAsync();
        var fe = new Asset { Name = "Extinguisher", Ref = "FE 3", Property = w.Task.Property };
        w.Db.ServiceTasks.Add(new ServiceTask { ClientTask = w.Task, Asset = fe, Routine = "10 - Portable Fire Extinguishers: Six-monthly" });
        await w.Db.SaveChangesAsync();
        var field = new FieldService(w.Db);

        var bad = await field.RecordAsync(w.Task.Id, w.Me.Id, fe.Id, "10 - Portable Fire Extinguishers: Six-monthly", "Maybe", null);
        var missing = await field.RecordAsync(w.Task.Id, w.Me.Id, fe.Id + 1, "10 - Portable Fire Extinguishers: Six-monthly", "Pass", null);
        await field.RecordAsync(w.Task.Id, w.Me.Id, fe.Id, "10 - Portable Fire Extinguishers: Six-monthly", "fail", "Gauge in the red");

        Assert.Equal(FieldOutcome.Invalid, bad.Outcome);
        Assert.Equal(FieldOutcome.NotFound, missing.Outcome);
        var line = await w.Db.ServiceTasks.SingleAsync();
        Assert.Equal(("Fail", "Gauge in the red", w.Me.Id), (line.Result, line.Notes, line.TechnicianId));
        Assert.NotNull(line.PerformedUtc);

        var vm = (await field.TaskAsync(w.Task.Id, w.Me.Id))!;
        Assert.Equal("Fail", Assert.Single(vm.Lines).Result);

        await field.RecordAsync(w.Task.Id, w.Me.Id, fe.Id, "10 - Portable Fire Extinguishers: Six-monthly", "", null);
        Assert.Equal((null, null), (line.Result, line.PerformedUtc));
    }

    [Fact]
    public async Task AClosedTask_TakesNoWork()
    {
        var w = await SetupAsync();
        w.Task.Status = ClientTaskStatus.Completed;
        await w.Db.SaveChangesAsync();
        var field = new FieldService(w.Db);

        var started = await field.StartAsync(w.Task.Id, w.Me.Id, "Performing Task", null);

        Assert.Equal(FieldOutcome.Invalid, started.Outcome);
        Assert.Equal("This task is closed.", (await field.TaskAsync(w.Task.Id, w.Me.Id))!.CannotWork);
    }

    [Fact]
    public async Task ATaskLine_IsTickedDoneToday_AndCanBeUnticked()
    {
        var w = await SetupAsync();
        var routine = new Routine { Name = "10 - Portable Fire Extinguishers: Six-monthly", Property = w.Task.Property, DueDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) };
        var line = new Subtask { ClientTask = w.Task, Routine = routine, Order = 1 };
        var removed = new Subtask { ClientTask = w.Task, Order = 2, DeletedUtc = DateTime.UtcNow };
        var otherTask = new Subtask { ClientTask = w.Elsewhere, Order = 1 };
        w.Db.AddRange(line, removed, otherTask);
        await w.Db.SaveChangesAsync();
        var field = new FieldService(w.Db);

        var vm = (await field.TaskAsync(w.Task.Id, w.Me.Id))!;
        var listed = Assert.Single(vm.TaskLines);
        Assert.Equal(("10 - Portable Fire Extinguishers: Six-monthly", false), (listed.Label, listed.IsPerformed));

        Assert.Equal(FieldOutcome.NotFound, (await field.SetLineDoneAsync(w.Task.Id, otherTask.Id, true)).Outcome);
        Assert.Equal(FieldOutcome.NotFound, (await field.SetLineDoneAsync(w.Task.Id, removed.Id, true)).Outcome);

        await field.SetLineDoneAsync(w.Task.Id, line.Id, true);
        Assert.True(line.IsPerformed);
        Assert.Equal(DateTime.SpecifyKind(DateTime.UtcNow.ToSydney().Date, DateTimeKind.Utc), line.PerformedDate);

        await field.SetLineDoneAsync(w.Task.Id, line.Id, false);
        Assert.Equal((false, null), (line.IsPerformed, line.PerformedDate));
    }

    [Fact]
    public async Task StockUsed_IsRecordedAsUptickDoes_AndOnlyTheTechsOwnCanBeRemoved()
    {
        var w = await SetupAsync();
        var tags = new Product { Name = "Maintenance tag", CostPrice = 0.5m };
        var retired = new Product { Name = "Old gauge", Active = false };
        w.Db.AddRange(tags, retired);
        var uptickUse = new TaskStockTransaction { UptickId = 5, ClientTask = w.Task, Product = tags, Quantity = -3, OtherType = "servicetask", UptickUserId = w.Me.Id };
        w.Db.Add(uptickUse);
        await w.Db.SaveChangesAsync();
        var field = new FieldService(w.Db);

        Assert.Equal(FieldOutcome.Invalid, (await field.UseStockAsync(w.Task.Id, w.Me.Id, tags.Id, 0)).Outcome);
        Assert.Equal(FieldOutcome.Invalid, (await field.UseStockAsync(w.Task.Id, w.Me.Id, retired.Id, 1)).Outcome);
        Assert.Equal(FieldOutcome.Done, (await field.UseStockAsync(w.Task.Id, w.Me.Id, tags.Id, 12)).Outcome);
        await field.UseStockAsync(w.Task.Id, w.Other.Id, tags.Id, 2);

        var mine = await w.Db.TaskStockTransactions.SingleAsync(s => s.UptickId == null && s.UptickUserId == w.Me.Id);
        Assert.Equal((-12m, "servicetask", 0.5m), (mine.Quantity, mine.OtherType, mine.CostPrice));

        var vm = (await field.TaskAsync(w.Task.Id, w.Me.Id))!;
        Assert.Equal(new[] { (3m, false), (12m, true), (2m, false) }, vm.StockUsed.OrderBy(s => s.Id).Select(s => (s.Quantity, s.Removable)));
        Assert.Equal(new[] { "Maintenance tag" }, vm.Products.Select(p => p.Name));

        var others = await w.Db.TaskStockTransactions.SingleAsync(s => s.UptickUserId == w.Other.Id);
        Assert.Equal(FieldOutcome.NotFound, (await field.RemoveStockAsync(w.Task.Id, w.Me.Id, uptickUse.Id)).Outcome);
        Assert.Equal(FieldOutcome.NotFound, (await field.RemoveStockAsync(w.Task.Id, w.Me.Id, others.Id)).Outcome);
        Assert.Equal(FieldOutcome.Done, (await field.RemoveStockAsync(w.Task.Id, w.Me.Id, mine.Id)).Outcome);
        Assert.Equal(2, await w.Db.TaskStockTransactions.CountAsync());
    }

    [Fact]
    public async Task ATask_IsTheirs_WhenAssignedOrBooked()
    {
        var w = await SetupAsync();
        w.Db.Appointments.Add(new Appointment { ClientTask = w.Elsewhere, StartUtc = NineAmUtc, Technicians = [w.Other] });
        await w.Db.SaveChangesAsync();
        var field = new FieldService(w.Db);

        Assert.True(await field.IsTheirsAsync(w.Task.Id, w.Me.Id));
        Assert.True(await field.IsTheirsAsync(w.Elsewhere.Id, w.Other.Id));
        Assert.False(await field.IsTheirsAsync(w.Task.Id, w.Other.Id));
    }

    // A change held on the phone without signal is sent later with when it was made
    [Fact]
    public void WhenDone_TakesThePhonesTime_NeverInTheFuture_NorAWeekOut()
    {
        var now = new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc);
        long Ms(DateTime d) => new DateTimeOffset(d).ToUnixTimeMilliseconds();

        Assert.Null(FieldService.WhenDone(null, now));
        Assert.Equal(now.AddHours(-3), FieldService.WhenDone(Ms(now.AddHours(-3)), now));
        Assert.Equal(now, FieldService.WhenDone(Ms(now.AddMinutes(10)), now));
        Assert.Null(FieldService.WhenDone(Ms(now.AddDays(-8)), now));
        Assert.Null(FieldService.WhenDone(-1, now));
    }

    [Fact]
    public async Task HeldChanges_AreRecordedAtWhenTheyWereMade()
    {
        var w = await SetupAsync();
        var field = new FieldService(w.Db);
        var start = DateTime.UtcNow.AddHours(-3);

        await field.StartAsync(w.Task.Id, w.Me.Id, "Performing Task", null, start);
        await field.FinishAsync(w.Task.Id, w.Me.Id, null, null, start.AddHours(2));

        var session = await w.Db.TaskSessions.SingleAsync();
        Assert.Equal(start, session.StartedUtc);
        Assert.Equal(2m, session.Hours);

        // A stop sent after a later start: never a negative time
        await field.StartAsync(w.Task.Id, w.Me.Id, "Performing Task", null, start.AddHours(2.5));
        await field.FinishAsync(w.Task.Id, w.Me.Id, null, null, start.AddHours(2));
        var second = await w.Db.TaskSessions.OrderBy(s => s.Id).LastAsync();
        Assert.Equal(0m, second.Hours);
    }
}
