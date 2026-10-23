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
    Task<FieldResult> StartAsync(int taskId, int technicianId, string type, Coords? at, CancellationToken ct = default);

    Task<FieldResult> FinishAsync(int taskId, int technicianId, Coords? at, string? note, CancellationToken ct = default);

    // Pass, Fail or N/A for an asset serviced for one of the task's routines (none clears it)
    Task<FieldResult> RecordAsync(int taskId, int technicianId, int assetId, string routine, string? result, string? notes, CancellationToken ct = default);
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

        return new FieldDayVm
        {
            Day = day,
            Technician = await _db.UptickUsers.Where(u => u.Id == technicianId).Select(u => u.Name).FirstOrDefaultAsync(ct),
            Visits = visits,
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
            Lines = lines.Select(l =>
            {
                saved.TryGetValue((l.Asset.Id, l.Routine), out var s);
                return new FieldLine(l.Asset.Id, l.Asset.Name, l.Asset.Ref, l.Asset.Location, l.Routine, s?.Result, s?.Notes,
                    byAsset[l.Asset.Id].ToList());
            }).ToList(),
            OtherRemarks = remarks.Where(r => r.AssetId == null || !assetIds.Contains(r.AssetId.Value)).Select(r => r.Remark).ToList()
        };

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
        return vm;
    }

    public Task<bool> IsTheirsAsync(int taskId, int technicianId, CancellationToken ct = default) =>
        _db.ClientTasks.AnyAsync(t => t.Id == taskId &&
            (t.AssignedToUptickUserId == technicianId
             || _db.Appointments.Any(a => a.ClientTaskId == taskId && a.Technicians.Any(u => u.Id == technicianId))), ct);

    public async Task<FieldResult> StartAsync(int taskId, int technicianId, string type, Coords? at, CancellationToken ct = default)
    {
        var sessionType = SessionTypes.FirstOrDefault(s => s.Type == type);
        if (sessionType.Type == null) return new(FieldOutcome.Invalid, "Pick what you're doing.");
        var task = await _db.ClientTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!IsOpen(task))
            return new(FieldOutcome.Invalid, "This task is closed.");

        var now = DateTime.UtcNow;
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

    public async Task<FieldResult> FinishAsync(int taskId, int technicianId, Coords? at, string? note, CancellationToken ct = default)
    {
        var running = (await RunningAsync(technicianId, ct)).Where(s => s.ClientTaskId == taskId).ToList();
        if (running.Count == 0) return new(FieldOutcome.Invalid, "Your time isn't running on this task.");
        var now = DateTime.UtcNow;
        foreach (var s in running) Finish(s, now, at, note);
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public async Task<FieldResult> RecordAsync(int taskId, int technicianId, int assetId, string routine, string? result, string? notes, CancellationToken ct = default)
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
        line.PerformedUtc = chosen == null ? null : DateTime.UtcNow;
        line.TechnicianId = chosen == null ? null : technicianId;
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    public static bool IsOpen(ClientTask task) =>
        task.IsActive && task.Status is not (ClientTaskStatus.Completed or ClientTaskStatus.Cancelled);

    private static void Finish(TaskSession s, DateTime now, Coords? at, string? note)
    {
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
