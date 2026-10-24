using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Files;
using FyreApp.ViewModels.Field;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Field;

public interface IFieldAssetService
{
    // An asset at the job's property, as the field app shows it; null when the task or the asset isn't there
    Task<FieldAssetVm?> AssetAsync(int taskId, int assetId, int? technicianId, CancellationToken ct = default);

    // Photos of the asset, taken on the job. Returns why any were left out.
    Task<(FieldResult Result, IReadOnlyList<string> Problems)> AddPhotosAsync(int taskId, int assetId, IReadOnlyList<IFormFile> photos,
        DateTime? when = null, CancellationToken ct = default);
}

// One asset in the field app: what it is, its result on the job (saved as on the job's page), its remarks, its photos
// (Uptick's and those taken in FyreApp, kept as UptickFile rows with Source "fyreapp" on the asset) and its history:
// service on every task, past remarks, and its Timeline.
public class FieldAssetService : IFieldAssetService
{
    // Asset photos have no "as found" / "once fixed"
    public const string PhotoType = "asset";

    private const int HistorySize = 50;

    private readonly AppDbContext _db;
    private readonly IFieldService _field;
    private readonly IFileStore _store;

    public FieldAssetService(AppDbContext db, IFieldService field, IFileStore store)
    {
        _db = db;
        _field = field;
        _store = store;
    }

    public async Task<FieldAssetVm?> AssetAsync(int taskId, int assetId, int? technicianId, CancellationToken ct = default)
    {
        var job = await _field.TaskAsync(taskId, technicianId, ct);
        if (job == null) return null;
        var asset = await _db.Assets.AsNoTracking()
            .Include(a => a.AssetTypes)
            .Include(a => a.AssetTypeVariant)
            .Include(a => a.LastServicedBy)
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PropertyId == job.Task.PropertyId, ct);
        if (asset == null) return null;

        var lines = job.Lines.Where(l => l.AssetId == assetId).ToList();
        var service = (await _db.ServiceTasks.AsNoTracking()
                .Where(s => s.AssetId == assetId && s.Result != null)
                .OrderByDescending(s => s.PerformedUtc ?? s.ClientTask.DueDateUtc).ThenByDescending(s => s.Id)
                .Take(HistorySize)
                .Select(s => new
                {
                    s.PerformedUtc, s.ClientTaskId, s.ClientTask.Ref, s.ClientTask.UptickId, s.ClientTask.FyreRef, s.Routine, s.Result, s.Notes,
                    By = s.Technician != null ? s.Technician.Name : null
                })
                .ToListAsync(ct))
            .Select(s => new FieldAssetServiceLine(s.PerformedUtc, s.ClientTaskId,
                new ClientTask { Ref = s.Ref, UptickId = s.UptickId, FyreRef = s.FyreRef }.DisplayRef, s.Routine, s.Result, s.Notes, s.By))
            .ToList();

        return new FieldAssetVm
        {
            Job = job,
            Asset = asset,
            Lines = lines,
            // On the job's list, its remarks come with its lines; otherwise, those open on it
            OpenRemarks = lines.FirstOrDefault()?.Remarks ?? (await _db.Remarks.AsNoTracking()
                    .Where(d => d.AssetId == assetId && d.Active)
                    .OrderBy(d => d.RaisedUtc).ThenBy(d => d.Id)
                    .ToListAsync(ct))
                .Select(d => new FieldRemark(d.Id, d.DisplayRef, d.RemarkType, d.Description, d.SeverityLabel, d.RaisedOnTaskId == taskId))
                .ToList(),
            Service = service,
            PastRemarks = await _db.Remarks.AsNoTracking()
                .Where(d => d.AssetId == assetId && !d.Active)
                .OrderByDescending(d => d.InactiveUtc ?? d.RaisedUtc).ThenByDescending(d => d.Id)
                .Take(HistorySize)
                .ToListAsync(ct),
            Timeline = await _db.TimelineEvents.AsNoTracking()
                .Where(e => e.AssetId == assetId)
                .OrderByDescending(e => e.WhenUtc).ThenByDescending(e => e.Id)
                .Take(HistorySize)
                .ToListAsync(ct)
        };
    }

    public async Task<(FieldResult Result, IReadOnlyList<string> Problems)> AddPhotosAsync(int taskId, int assetId, IReadOnlyList<IFormFile> photos,
        DateTime? when = null, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return (new(FieldOutcome.NotFound), []);
        if (!FieldService.IsOpen(task)) return (new(FieldOutcome.Invalid, "This task is closed."), []);
        if (!await _db.Assets.AnyAsync(a => a.Id == assetId && a.PropertyId == task.PropertyId, ct))
            return (new(FieldOutcome.NotFound, "That asset isn't at this property."), []);
        if (!photos.Any(p => p.Length > 0)) return (new(FieldOutcome.Invalid, "Take or pick a photo first."), []);

        var problems = await FyreAppFiles.AddPhotosAsync(_db, _store, UptickResources.Assets, assetId, PhotoType, photos, when ?? DateTime.UtcNow, ct);
        await _db.SaveChangesAsync(ct);
        return (FieldResult.Done, problems);
    }
}
