using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Field;

public sealed record FinishResult(FieldResult Result, int? ReportId = null);

public interface IFieldFinishService
{
    // Completes the task in the field: stops the technician's clock on it, closes it (as completing it anywhere does)
    // and writes its service report from the assets' results, with the technician's note
    Task<FinishResult> CompleteAsync(int taskId, int technicianId, string? note, Coords? at, CancellationToken ct = default);
}

// Finishing a job in the field app. The report is FyreApp's (no Uptick id, a FyreRef such as "SR-1001"): one line per
// asset given a result, failed if it failed for any routine. Completing a task that already has a report makes an
// amendment. Remarks, forms and the sign-off stay on the task; the report's page shows them from there.
public class FieldFinishService : IFieldFinishService
{
    public const string ReportType = "Service Report";

    private readonly AppDbContext _db;
    private readonly IClientTaskService _tasks;

    public FieldFinishService(AppDbContext db, IClientTaskService tasks)
    {
        _db = db;
        _tasks = tasks;
    }

    public async Task<FinishResult> CompleteAsync(int taskId, int technicianId, string? note, Coords? at, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(new(FieldOutcome.NotFound));
        if (!FieldService.IsOpen(task)) return new(new(FieldOutcome.Invalid, "This task is closed."));
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 4000) return new(new(FieldOutcome.Invalid, "The note is too long."));

        var now = DateTime.UtcNow;
        // A calendar date where it was done (dates are kept as UTC midnight)
        var today = DateTime.SpecifyKind(now.ToSydney().Date, DateTimeKind.Utc);

        // Their clock stops here (Uptick's own sessions are Uptick's)
        foreach (var running in await _db.TaskSessions
                     .Where(s => s.ClientTaskId == taskId && s.UptickUserId == technicianId && s.UptickId == null
                                 && s.FinishedUtc == null && s.TrashedUtc == null)
                     .ToListAsync(ct))
            FieldService.Finish(running, now, at, null);

        var results = await _db.ServiceTasks.AsNoTracking()
            .Where(s => s.ClientTaskId == taskId && s.AssetId != null && s.Result != null)
            .Select(s => new { Asset = s.Asset!, s.Result, s.Notes, s.PerformedUtc })
            .ToListAsync(ct);
        var items = results
            .GroupBy(r => r.Asset.Id)
            .Select(g =>
            {
                var a = g.First().Asset;
                return new ReportItem
                {
                    AssetId = a.Id,
                    Label = string.IsNullOrWhiteSpace(a.Ref) ? a.Name : $"{a.Ref} - {a.Name}",
                    Ref = a.Ref,
                    Location = a.Location,
                    Barcode = a.Barcode,
                    Make = a.Make,
                    Model = a.Model,
                    Size = a.Size,
                    Compliant = g.Any(r => r.Result == "Fail") ? false : g.Any(r => r.Result == "Pass") ? true : null,
                    Notes = Join(g.Select(r => r.Notes)),
                    InspectedDate = Day(g.Max(r => r.PerformedUtc)) ?? today,
                    ServicedDate = Day(g.Max(r => r.PerformedUtc)) ?? today
                };
            })
            .ToList();

        var report = new Report
        {
            PropertyId = task.PropertyId,
            ClientTaskId = task.Id,
            ReportType = ReportType,
            TaskRef = task.DisplayRef,
            TaskName = task.Title,
            InspectedDate = today,
            IssuedDate = today,
            Technician = await _db.UptickUsers.Where(u => u.Id == technicianId).Select(u => u.Name).FirstOrDefaultAsync(ct),
            Compliant = items.Any(i => i.Compliant == false) ? false : items.Any(i => i.Compliant == true) ? true : null,
            GeneralNote = note,
            // Not sent to the customer yet
            Published = false,
            Amendment = await _db.Reports.AnyAsync(r => r.ClientTaskId == task.Id, ct)
        };
        report.Author = report.Technician;
        _db.Reports.Add(report);
        foreach (var i in items)
        {
            i.Report = report;
            _db.ReportItems.Add(i);
        }

        task.PerformedDate = today;
        await _db.SaveChangesAsync(ct);

        // Closed as completing it anywhere else does (inactive, its routine schedules moved on)
        await _tasks.CompleteAsync(task.Id);
        return new(FieldResult.Done, report.Id);
    }

    private static string? Join(IEnumerable<string?> notes)
    {
        var text = string.Join("; ", notes.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
        return text.Length == 0 ? null : text.Length > 2000 ? text[..2000] : text;
    }

    private static DateTime? Day(DateTime? utc) =>
        utc is { } d ? DateTime.SpecifyKind(d.ToSydney().Date, DateTimeKind.Utc) : null;
}
