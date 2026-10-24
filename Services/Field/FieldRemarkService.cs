using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Files;
using FyreApp.ViewModels.Field;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Field;

public sealed record RemarkSaved(FieldResult Result, int? RemarkId = null, IReadOnlyList<string>? PhotoProblems = null);

public interface IFieldRemarkService
{
    // Null when the task, or the asset at its property, isn't there
    Task<RaiseRemarkVm?> RaiseFormAsync(int taskId, int? assetId, CancellationToken ct = default);

    // Raises a remark on the task (as found), with its photos
    Task<RemarkSaved> RaiseAsync(int taskId, RaiseRemarkInput input, IReadOnlyList<IFormFile> photos, int? technicianId = null, DateTime? when = null, CancellationToken ct = default);

    // Null when the remark isn't an open one at the task's property
    Task<ResolveRemarkVm?> ResolveFormAsync(int taskId, int remarkId, CancellationToken ct = default);

    // Closes an open remark at the task's property (fixed on this task), with photos once fixed
    Task<RemarkSaved> ResolveAsync(int taskId, int remarkId, string? resolution, IReadOnlyList<IFormFile> photos, int? technicianId = null, DateTime? when = null, CancellationToken ct = default);
}

// Remarks in the field app: raising one on an asset (or the property) with photos as found, resolving one with
// photos once fixed. A remark raised here is FyreApp's (no Uptick id); resolving a migrated one is a local edit.
public class FieldRemarkService : IFieldRemarkService
{
    // Uptick's severities
    public static readonly IReadOnlyList<(int Value, string Label)> Severities =
    [
        (0, "Informational"), (1, "Recommendation"), (2, "Non-conformance"), (5, "Non-critical defect"), (10, "Critical defect")
    ];

    public const string NeedsQuoting = "Needs Quoting";
    public const string Resolved = "Resolved";

    private readonly AppDbContext _db;
    private readonly IFileStore _store;

    public FieldRemarkService(AppDbContext db, IFileStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<RaiseRemarkVm?> RaiseFormAsync(int taskId, int? assetId, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().Include(t => t.Property).FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return null;
        Asset? asset = null;
        if (assetId is { } id)
        {
            asset = await _db.Assets.AsNoTracking().Include(a => a.AssetTypes)
                .FirstOrDefaultAsync(a => a.Id == id && a.PropertyId == task.PropertyId, ct);
            if (asset == null) return null;
        }

        var assetTypes = asset?.AssetTypes.Select(t => t.Id).ToHashSet() ?? [];
        var types = (await _db.RemarkTypes.AsNoTracking()
                .Where(t => t.Active)
                .Select(t => new { t.Id, t.Label, t.SeverityLabel, t.AssetTypeId })
                .ToListAsync(ct))
            .Select(t => new RemarkTypeOption(t.Id, t.Label, t.SeverityLabel, t.AssetTypeId is { } at && assetTypes.Contains(at)))
            .OrderByDescending(t => t.ForAsset).ThenBy(t => t.Label)
            .ToList();

        return new RaiseRemarkVm
        {
            TaskId = task.Id,
            TaskRef = task.DisplayRef,
            PropertyName = task.Property.Name,
            Asset = asset,
            Types = types,
            Input = new RaiseRemarkInput { AssetId = asset?.Id, Location = asset?.Location }
        };
    }

    public async Task<RemarkSaved> RaiseAsync(int taskId, RaiseRemarkInput input, IReadOnlyList<IFormFile> photos, int? technicianId = null, DateTime? when = null, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(new(FieldOutcome.NotFound));
        if (!FieldService.IsOpen(task)) return new(new(FieldOutcome.Invalid, "This task is closed."));

        Asset? asset = null;
        if (input.AssetId is { } assetId)
        {
            asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId && a.PropertyId == task.PropertyId, ct);
            if (asset == null) return new(new(FieldOutcome.NotFound, "That asset isn't at this property."));
        }
        RemarkType? type = null;
        if (input.RemarkTypeId is { } typeId)
        {
            type = await _db.RemarkTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct);
            if (type == null) return new(new(FieldOutcome.Invalid, "Pick a remark type from the list."));
        }
        // Not on the list: their own words as its type
        var custom = type == null ? Clean(input.CustomType) : null;
        if (custom?.Length > 200) return new(new(FieldOutcome.Invalid, "The remark type is too long."));
        if (type == null && custom == null && string.IsNullOrWhiteSpace(input.Description))
            return new(new(FieldOutcome.Invalid, "Pick a remark type or describe the problem."));

        var severity = input.Severity is { } s
            ? Severities.FirstOrDefault(x => x.Value == s)
            : Severities.FirstOrDefault(x => string.Equals(x.Label, type?.SeverityLabel, StringComparison.OrdinalIgnoreCase));
        if (input.Severity != null && severity.Label == null) return new(new(FieldOutcome.Invalid, "Pick a severity from the list."));

        var now = when ?? DateTime.UtcNow;
        var remark = new Remark
        {
            PropertyId = task.PropertyId,
            AssetId = asset?.Id,
            RemarkTypeId = type?.Id,
            RemarkType = type?.Label ?? custom,
            Severity = severity.Label == null ? null : severity.Value,
            SeverityLabel = severity.Label,
            Description = Clean(input.Description) ?? type?.Label ?? custom,
            Location = Clean(input.Location),
            Status = NeedsQuoting,
            Active = true,
            RaisedUtc = now,
            RaisedOnTaskId = task.Id
        };
        _db.Remarks.Add(remark);
        _db.RemarkEvents.Add(new RemarkEvent { Remark = remark, Event = "A", Label = "Added", WhenUtc = now, UptickUserId = technicianId, ClientTaskId = task.Id });
        await _db.SaveChangesAsync(ct);

        var problems = await FyreAppFiles.AddPhotosAsync(_db, _store, UptickResources.Remarks, remark.Id, FyreAppFiles.Open, photos, now, ct);
        await _db.SaveChangesAsync(ct);
        return new(FieldResult.Done, remark.Id, problems);
    }

    public async Task<ResolveRemarkVm?> ResolveFormAsync(int taskId, int remarkId, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return null;
        var remark = await _db.Remarks.AsNoTracking()
            .Include(r => r.Asset).Include(r => r.RemarkTypeRef)
            .FirstOrDefaultAsync(r => r.Id == remarkId && r.Active && r.PropertyId == task.PropertyId, ct);
        return remark == null ? null : new ResolveRemarkVm
        {
            TaskId = task.Id,
            TaskRef = task.DisplayRef,
            Remark = remark,
            Resolution = remark.RemarkTypeRef?.Resolution
        };
    }

    public async Task<RemarkSaved> ResolveAsync(int taskId, int remarkId, string? resolution, IReadOnlyList<IFormFile> photos, int? technicianId = null, DateTime? when = null, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(new(FieldOutcome.NotFound));
        if (!FieldService.IsOpen(task)) return new(new(FieldOutcome.Invalid, "This task is closed."));
        var remark = await _db.Remarks.FirstOrDefaultAsync(r => r.Id == remarkId && r.PropertyId == task.PropertyId, ct);
        if (remark == null) return new(new(FieldOutcome.NotFound, "That remark isn't at this property."));
        if (!remark.Active) return new(new(FieldOutcome.Invalid, "That remark is already resolved."));
        if (string.IsNullOrWhiteSpace(resolution)) return new(new(FieldOutcome.Invalid, "Say how it was fixed."));

        var now = when ?? DateTime.UtcNow;
        remark.Active = false;
        remark.Status = Resolved;
        remark.InactiveUtc = now;
        remark.Resolution = resolution.Trim();
        _db.RemarkEvents.Add(new RemarkEvent { RemarkId = remark.Id, Event = "R", Label = Resolved, Notes = remark.Resolution, WhenUtc = now, UptickUserId = technicianId, ClientTaskId = task.Id });
        await _db.SaveChangesAsync(ct);

        var problems = await FyreAppFiles.AddPhotosAsync(_db, _store, UptickResources.Remarks, remark.Id, FyreAppFiles.Fixed, photos, now, ct);
        await _db.SaveChangesAsync(ct);
        return new(FieldResult.Done, remark.Id, problems);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
