using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.ViewModels.Field;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Field;

public enum FieldOutcome { Done, NotFound, NotAllowed, Invalid }
public sealed record FieldResult(FieldOutcome Outcome, string? Message = null)
{
    public static readonly FieldResult Done = new(FieldOutcome.Done);
}

// Where the phone was, when it said
public sealed record Coords(double Latitude, double Longitude);

public interface IFieldService
{
    // A technician's visits on a day (Sydney time)
    Task<FieldDayVm> DayAsync(int technicianId, DateTime day, CancellationToken ct = default);

    // The task as the field app shows it; technicianId is the signed-in login's Uptick user (none: look only)
    Task<FieldTaskVm?> TaskAsync(int taskId, int? technicianId, CancellationToken ct = default);

    // The task is assigned to the technician, or they're booked on one of its appointments
    Task<bool> IsTheirsAsync(int taskId, int technicianId, CancellationToken ct = default);

    // Starts the technician's clock on the task (finishing it wherever it was running)
    Task<FieldResult> StartAsync(int taskId, int technicianId, string type, Coords? at, DateTime? when = null, CancellationToken ct = default);

    Task<FieldResult> FinishAsync(int taskId, int technicianId, Coords? at, string? note, DateTime? when = null, CancellationToken ct = default);

    // Pass, Fail or N/A for an asset serviced for one of the task's routines (none clears it)
    Task<FieldResult> RecordAsync(int taskId, int technicianId, int assetId, string routine, string? result, string? notes, DateTime? when = null, CancellationToken ct = default);

    // Ticks one of the task's lines (subtasks) done today, or not done
    Task<FieldResult> SetLineDoneAsync(int taskId, int subtaskId, bool done, DateTime? when = null, CancellationToken ct = default);

    // Records a product used on the task, as Uptick does: stock off the task onto a service line
    Task<FieldResult> UseStockAsync(int taskId, int technicianId, int productId, decimal quantity, DateTime? when = null, CancellationToken ct = default);

    // Takes back stock the technician recorded as used (in FyreApp) by mistake
    Task<FieldResult> RemoveStockAsync(int taskId, int technicianId, int useId, CancellationToken ct = default);
}

// The technicians' field app (/Field): their day, a task's details, their time on it and the assets they service.
// What it records is FyreApp's own (no Uptick id), so Uptick migration runs leave it alone; results on a migrated
// service task are kept as local edits.
public class FieldService : IFieldService
{
    // Uptick's session types a technician picks from in the field, and whether they're on site
    public static readonly IReadOnlyList<(string Type, bool OnSite)> SessionTypes =
        [("Travelling", false), ("Performing Task", true)];

    public static readonly IReadOnlyList<string> Results = ["Pass", "Fail", "N/A"];

    private readonly AppDbContext _db;
    public FieldService(AppDbContext db) => _db = db;

    public async Task<FieldDayVm> DayAsync(int technicianId, DateTime day, CancellationToken ct = default)
    {
        day = day.Date;
        var fromUtc = SydneyToUtc(day);
        var toUtc = SydneyToUtc(day.AddDays(1));
        var dayUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc);   // dates are kept as UTC midnight

        var booked = await _db.Appointments.AsNoTracking()
            .Where(a => a.ClientTaskId != null && a.StartUtc >= fromUtc && a.StartUtc < toUtc
                        && a.Status != "cancelled" && a.Technicians.Any(t => t.Id == technicianId))
            .Select(a => new { a.StartUtc, a.EndUtc, a.Notes, a.Address, Task = a.ClientTask!, a.ClientTask!.Property.Name, PropertyAddress = a.ClientTask.Property.AddressDisplay })
            .ToListAsync(ct);
        var bookedIds = booked.Select(b => b.Task.Id).ToHashSet();

        var scheduled = await _db.ClientTasks.AsNoTracking()
            .Where(t => t.AssignedToUptickUserId == technicianId && t.IsActive && t.ScheduledDate == dayUtc && !bookedIds.Contains(t.Id))
            .Select(t => new { Task = t, t.Property.Name, t.Property.AddressDisplay })
            .ToListAsync(ct);

        var visits = booked
            .Select(b => new FieldVisit(b.Task.Id, b.Task.DisplayRef, b.Task.Title, b.Task.Category, b.Name, b.Address ?? b.PropertyAddress,
                b.StartUtc.ToSydney(), b.EndUtc.ToSydney(), b.Notes, b.Task.Status))
            .OrderBy(v => v.StartLocal)
            .Concat(scheduled.OrderBy(s => s.Name)
                .Select(s => new FieldVisit(s.Task.Id, s.Task.DisplayRef, s.Task.Title, s.Task.Category, s.Name, s.AddressDisplay,
                    null, null, null, s.Task.Status)))
            .ToList();

        // Their other open tasks, whatever the day
        var onTheDay = visits.Select(v => v.TaskId).ToHashSet();
        var assigned = (await _db.ClientTasks.AsNoTracking()
                .Where(t => t.AssignedToUptickUserId == technicianId && t.IsActive && !onTheDay.Contains(t.Id)
                            && t.Status != ClientTaskStatus.Completed && t.Status != ClientTaskStatus.Cancelled)
                .Select(t => new { Task = t, t.Property.Name, t.Property.AddressDisplay })
                .ToListAsync(ct))
            .Select(s => new FieldVisit(s.Task.Id, s.Task.DisplayRef, s.Task.Title, s.Task.Category, s.Name, s.AddressDisplay,
                null, null, null, s.Task.Status, s.Task.ScheduledDate, s.Task.DueDateUtc.ToSydney()?.Date))
            .OrderBy(v => v.Scheduled ?? v.Due ?? DateTime.MaxValue)
            .ThenBy(v => v.Due ?? DateTime.MaxValue)
            .ThenBy(v => v.PropertyName)
            .ToList();

        return new FieldDayVm
        {
            Day = day,
            Technician = await _db.UptickUsers.Where(u => u.Id == technicianId).Select(u => u.Name).FirstOrDefaultAsync(ct),
            Visits = visits,
            Assigned = assigned,
            Working = await RunningVisitAsync(technicianId, null, ct)
        };
    }

    public async Task<FieldTaskVm?> TaskAsync(int taskId, int? technicianId, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking()
            .Include(t => t.Client)
            .Include(t => t.Property)
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return null;

        var lines = await TaskServiceLines.ForTaskAsync(_db, task, ct);
        var assetIds = lines.Select(l => l.Asset.Id).Distinct().ToList();
        var saved = (await _db.ServiceTasks.AsNoTracking()
                .Where(s => s.ClientTaskId == taskId && s.AssetId != null)
                .Select(s => new { AssetId = s.AssetId!.Value, s.Routine, s.Result, s.Notes })
                .ToListAsync(ct))
            .DistinctBy(s => (s.AssetId, s.Routine))
            .ToDictionary(s => (s.AssetId, s.Routine));
        // Open remarks at the property: on the assets being serviced, and any others raised on this task
        var remarks = (await _db.Remarks.AsNoTracking()
                .Where(d => d.Active && d.PropertyId == task.PropertyId
                            && ((d.AssetId != null && assetIds.Contains(d.AssetId.Value)) || d.RaisedOnTaskId == taskId))
                .OrderBy(d => d.RaisedUtc).ThenBy(d => d.Id)
                .Select(d => new { d.Id, d.UptickId, d.FyreRef, d.AssetId, d.RemarkType, d.Description, d.SeverityLabel, d.RaisedOnTaskId })
                .ToListAsync(ct))
            .Select(d => (d.AssetId, Remark: new FieldRemark(d.Id, new Remark { UptickId = d.UptickId, FyreRef = d.FyreRef }.DisplayRef,
                d.RemarkType, d.Description, d.SeverityLabel, d.RaisedOnTaskId == taskId)))
            .ToList();
        var byAsset = remarks.Where(r => r.AssetId != null)
            .ToLookup(r => r.AssetId!.Value, r => r.Remark);

        var vm = new FieldTaskVm
        {
            Task = task,
            Contacts = await _db.PropertyContacts.AsNoTracking()
                .Where(c => c.PropertyId == task.PropertyId && c.Active)
                .OrderBy(c => c.Name)
                .ToListAsync(ct),
            Appointments = await _db.Appointments.AsNoTracking()
                .Include(a => a.Technicians)
                .Where(a => a.ClientTaskId == taskId && a.Status != "cancelled")
                .OrderBy(a => a.StartUtc)
                .ToListAsync(ct),
            // (Uptick can hold the same asset twice for a routine: listed once)
            Lines = lines.DistinctBy(l => (l.Asset.Id, l.Routine)).Select(l =>
            {
                saved.TryGetValue((l.Asset.Id, l.Routine), out var s);
                return new FieldLine(l.Asset.Id, l.Asset.Name, l.Asset.Ref, l.Asset.Location, l.Routine, s?.Result, s?.Notes,
                    byAsset[l.Asset.Id].ToList());
            }).ToList(),
            OtherRemarks = remarks.Where(r => r.AssetId == null || !assetIds.Contains(r.AssetId.Value)).Select(r => r.Remark).ToList()
        };

        vm.TaskLines = (await _db.Subtasks.AsNoTracking()
                .Where(s => s.ClientTaskId == taskId && s.DeletedUtc == null)
                .OrderBy(s => s.Order).ThenBy(s => s.Id)
                .Select(s => new { s.Id, Routine = s.Routine != null ? s.Routine.Name : null, Due = s.Routine != null ? s.Routine.DueDate : (DateTime?)null, Product = s.Product != null ? s.Product.Name : null, s.IsPerformed })
                .ToListAsync(ct))
            .Select(s => new FieldTaskLine(s.Id, s.Routine ?? s.Product ?? "Line", s.Due, s.IsPerformed))
            .ToList();
        vm.StockUsed = (await _db.TaskStockTransactions.AsNoTracking()
                .Where(s => s.ClientTaskId == taskId && s.Quantity < 0)
                .OrderBy(s => s.CreatedUtc).ThenBy(s => s.Id)
                .Select(s => new { s.Id, s.UptickId, Product = s.Product != null ? s.Product.Name : null, s.Quantity, s.UptickUserId, By = s.UptickUser != null ? s.UptickUser.Name : null, s.CreatedUtc })
                .ToListAsync(ct))
            .Select(s => new FieldStockUse(s.Id, s.Product ?? "Unknown product", -s.Quantity, s.By, s.CreatedUtc,
                s.UptickId == null && technicianId != null && s.UptickUserId == technicianId))
            .ToList();

        if (technicianId is not { } me)
        {
            vm.CannotWork = "Your login isn't linked to an Uptick user, so you can look but not record work.";
            return vm;
        }
        if (!IsOpen(task))
            vm.CannotWork = "This task is closed.";

        var todayUtc = SydneyToUtc(DateTime.UtcNow.ToSydney().Date);
        vm.MyTimeToday = await _db.TaskSessions.AsNoTracking()
            .Where(s => s.ClientTaskId == taskId && s.UptickUserId == me && s.TrashedUtc == null && s.StartedUtc >= todayUtc)
            .OrderBy(s => s.StartedUtc)
            .ToListAsync(ct);
        vm.Running = vm.MyTimeToday.LastOrDefault(s => s.FinishedUtc == null && s.UptickId == null);
        if (vm.Running == null) vm.RunningElsewhere = await RunningVisitAsync(me, taskId, ct);
        if (vm.CannotWork == null)
            vm.Products = await _db.Products.AsNoTracking()
                .Where(p => p.Active)
                .OrderBy(p => p.Name)
                .Select(p => new FieldProduct(p.Id, p.Name, p.Code))
                .ToListAsync(ct);
        return vm;
    }

    public Task<bool> IsTheirsAsync(int taskId, int technicianId, CancellationToken ct = default) =>
        _db.ClientTasks.AnyAsync(t => t.Id == taskId &&
            (t.AssignedToUptickUserId == technicianId
             || _db.Appointments.Any(a => a.ClientTaskId == taskId && a.Technicians.Any(u => u.Id == technicianId))), ct);

    public async Task<FieldResult> StartAsync(int taskId, int technicianId, string type, Coords? at, DateTime? when = null, CancellationToken ct = default)
    {
        var sessionType = SessionTypes.FirstOrDefault(s => s.Type == type);
        if (sessionType.Type == null) return new(FieldOutcome.Invalid, "Pick what you're doing.");
        var task = await _db.ClientTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!IsOpen(task))
            return new(FieldOutcome.Invalid, "This task is closed.");

        var now = when ?? DateTime.UtcNow;
        foreach (var running in await RunningAsync(technicianId, ct))
            Finish(running, now, at, null);

        _db.TaskSessions.Add(new TaskSession
        {
            ClientTaskId = taskId,
            UptickUserId = technicianId,
            Type = sessionType.Type,
            IsOnsite = sessionType.OnSite,
            StartedUtc = now,
            StartedLatitude = at?.Latitude,
            StartedLongitude = at?.Longitude
        });
        if (task.Status == ClientTaskStatus.Open) task.Status = ClientTaskStatus.InProgress;
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public async Task<FieldResult> FinishAsync(int taskId, int technicianId, Coords? at, string? note, DateTime? when = null, CancellationToken ct = default)
    {
        var running = (await RunningAsync(technicianId, ct)).Where(s => s.ClientTaskId == taskId).ToList();
        if (running.Count == 0) return new(FieldOutcome.Invalid, "Your time isn't running on this task.");
        var now = when ?? DateTime.UtcNow;
        foreach (var s in running) Finish(s, now, at, note);
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public async Task<FieldResult> RecordAsync(int taskId, int technicianId, int assetId, string routine, string? result, string? notes, DateTime? when = null, CancellationToken ct = default)
    {
        string? chosen = null;
        if (!string.IsNullOrWhiteSpace(result))
        {
            chosen = Results.FirstOrDefault(r => string.Equals(r, result.Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosen == null) return new(FieldOutcome.Invalid, "Pass, Fail or N/A.");
        }
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!IsOpen(task))
            return new(FieldOutcome.Invalid, "This task is closed.");

        var rows = await _db.ServiceTasks.Where(s => s.ClientTaskId == taskId && s.AssetId != null).ToListAsync(ct);
        if (rows.Count == 0)
        {
            // The task's list was worked out from its routines: save it as it stands, so it stays the list worked on
            foreach (var (r, asset) in await TaskServiceLines.ForTaskAsync(_db, task, ct))
            {
                var row = new ServiceTask { ClientTaskId = taskId, AssetId = asset.Id, Routine = r };
                _db.ServiceTasks.Add(row);
                rows.Add(row);
            }
        }

        var line = rows.FirstOrDefault(s => s.AssetId == assetId && s.Routine == routine);
        if (line == null) return new(FieldOutcome.NotFound, "That asset isn't on this task.");

        line.Result = chosen;
        line.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        line.PerformedUtc = chosen == null ? null : when ?? DateTime.UtcNow;
        line.TechnicianId = chosen == null ? null : technicianId;
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public async Task<FieldResult> SetLineDoneAsync(int taskId, int subtaskId, bool done, DateTime? when = null, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!IsOpen(task)) return new(FieldOutcome.Invalid, "This task is closed.");
        var line = await _db.Subtasks.FirstOrDefaultAsync(s => s.Id == subtaskId && s.ClientTaskId == taskId && s.DeletedUtc == null, ct);
        if (line == null) return new(FieldOutcome.NotFound, "That line isn't on this task.");

        line.IsPerformed = done;
        // A calendar date where it was done (dates are kept as UTC midnight)
        line.PerformedDate = done ? DateTime.SpecifyKind((when ?? DateTime.UtcNow).ToSydney().Date, DateTimeKind.Utc) : null;
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public async Task<FieldResult> UseStockAsync(int taskId, int technicianId, int productId, decimal quantity, DateTime? when = null, CancellationToken ct = default)
    {
        if (quantity <= 0 || quantity > 10000 || decimal.Round(quantity, 2) != quantity)
            return new(FieldOutcome.Invalid, "Enter how many were used.");
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!IsOpen(task)) return new(FieldOutcome.Invalid, "This task is closed.");
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.Active, ct);
        if (product == null) return new(FieldOutcome.Invalid, "Pick a product from the list.");

        _db.TaskStockTransactions.Add(new TaskStockTransaction
        {
            ClientTaskId = taskId,
            ProductId = product.Id,
            Quantity = -quantity,
            OtherType = "servicetask",
            CostPrice = product.CostPrice,
            UptickUserId = technicianId,
            CreatedUtc = when ?? DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public async Task<FieldResult> RemoveStockAsync(int taskId, int technicianId, int useId, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!IsOpen(task)) return new(FieldOutcome.Invalid, "This task is closed.");
        // Only what this technician recorded in FyreApp: Uptick's movements are Uptick's
        var use = await _db.TaskStockTransactions.FirstOrDefaultAsync(s => s.Id == useId && s.ClientTaskId == taskId
            && s.UptickId == null && s.UptickUserId == technicianId && s.Quantity < 0, ct);
        if (use == null) return new(FieldOutcome.NotFound, "That isn't stock you recorded on this task.");
        _db.TaskStockTransactions.Remove(use);
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    // When the technician pressed it, as the phone says (milliseconds since 1970), for a change held on the phone while
    // there was no signal and sent later. Never in the future; none (now) when the phone's clock is a week out.
    public static DateTime? WhenDone(long? doneAt, DateTime nowUtc)
    {
        if (doneAt is not { } ms || ms < 0 || ms > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()) return null;
        var at = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        if (at < nowUtc.AddDays(-7)) return null;
        return at > nowUtc ? nowUtc : at;
    }

    public static bool IsOpen(ClientTask task) =>
        task.IsActive && task.Status is not (ClientTaskStatus.Completed or ClientTaskStatus.Cancelled);

    internal static void Finish(TaskSession s, DateTime now, Coords? at, string? note)
    {
        // Sent late from the phone, a stop can come before a start recorded since: never a negative time
        if (now < s.StartedUtc) now = s.StartedUtc;
        s.FinishedUtc = now;
        s.Hours = Math.Round((decimal)(now - s.StartedUtc).TotalHours, 2);
        s.FinishedLatitude = at?.Latitude;
        s.FinishedLongitude = at?.Longitude;
        if (!string.IsNullOrWhiteSpace(note)) s.Description = note.Trim();
    }

    // The technician's running sessions made in FyreApp (Uptick's own are Uptick's to finish)
    private Task<List<TaskSession>> RunningAsync(int technicianId, CancellationToken ct) =>
        _db.TaskSessions.Where(s => s.UptickUserId == technicianId && s.UptickId == null && s.FinishedUtc == null && s.TrashedUtc == null)
            .ToListAsync(ct);

    private async Task<FieldVisit?> RunningVisitAsync(int technicianId, int? except, CancellationToken ct)
    {
        var running = await _db.TaskSessions.AsNoTracking()
            .Where(s => s.UptickUserId == technicianId && s.UptickId == null && s.FinishedUtc == null && s.TrashedUtc == null
                        && s.ClientTaskId != except)
            .OrderByDescending(s => s.StartedUtc)
            .Select(s => new { s.StartedUtc, s.Type, Task = s.ClientTask, s.ClientTask.Property.Name, s.ClientTask.Property.AddressDisplay })
            .FirstOrDefaultAsync(ct);
        return running == null ? null
            : new FieldVisit(running.Task.Id, running.Task.DisplayRef, running.Task.Title, running.Type, running.Name, running.AddressDisplay,
                running.StartedUtc.ToSydney(), null, null, running.Task.Status);
    }

    private static DateTime SydneyToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Sydney);

    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
}
