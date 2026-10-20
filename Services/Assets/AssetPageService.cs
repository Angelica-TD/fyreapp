using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Timeline;
using FyreApp.ViewModels.Assets;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Assets;

public interface IAssetPageService
{
    // The asset page with what its tab shows; null when there's no such asset
    Task<AssetPageVm?> LoadAsync(int id, string? tab, bool notesOnly, CancellationToken ct = default);

    // The Update form, filled from the asset; null when there's no such asset
    Task<AssetEditVm?> EditFormAsync(int id, CancellationToken ct = default);

    // Fills in the asset and the type / variant lists around a form posted back with errors
    Task FillEditFormAsync(AssetEditVm form, CancellationToken ct = default);

    // Saves the form; each field changed goes on the asset's Timeline under the author's name.
    // False when there's no such asset.
    Task<bool> SaveAsync(AssetEditVm form, string? author, CancellationToken ct = default);
}

public class AssetPageService : IAssetPageService
{
    private readonly AppDbContext _db;
    private readonly ITimelineService _timeline;

    public AssetPageService(AppDbContext db, ITimelineService timeline)
    {
        _db = db;
        _timeline = timeline;
    }

    private IQueryable<Asset> WithDetails() => _db.Assets
        .Include(a => a.Property).ThenInclude(s => s.Client)
        .Include(a => a.AssetTypes)
        .Include(a => a.AssetTypeVariant);

    public async Task<AssetPageVm?> LoadAsync(int id, string? tab, bool notesOnly, CancellationToken ct = default)
    {
        var asset = await WithDetails().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset == null) return null;
        tab = AssetPageVm.NormaliseTab(tab);

        // Uptick's severity: higher is worse (10 critical defect … 0 informational)
        var defects = await _db.Remarks.AsNoTracking()
            .Where(d => d.AssetId == id)
            .OrderByDescending(d => d.Active).ThenByDescending(d => d.RaisedUtc)
            .ToListAsync(ct);
        var open = defects.Where(d => d.Active).OrderByDescending(d => d.Severity ?? -1).ThenByDescending(d => d.RaisedUtc).ToList();

        return new AssetPageVm
        {
            Asset = asset,
            Tab = tab,
            MostSevereOpenRemark = open.FirstOrDefault(),
            OpenRemarks = open,
            Remarks = defects,
            LastServiceBy = tab == "asset" ? await LastServiceByAsync(asset, ct) : null,
            Timeline = tab == "asset" ? await _timeline.ForAssetAsync(asset, notesOnly, ct) : [],
            NotesOnly = notesOnly,
            ExtraFields = await FyreApp.Services.Imports.UptickReferenceValues.ExtraFieldsAsync(_db, ExtraField.AssetContentType, "assets", asset.UptickId, asset.UptickData, ct)
        };
    }

    // Who Uptick says last serviced it; for an asset from the file export (which doesn't say), the technician on
    // the property's report from that day
    private async Task<string?> LastServiceByAsync(Asset asset, CancellationToken ct) =>
        asset.LastServicedById is { } userId
            ? await _db.UptickUsers.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync(ct)
        : asset.LastServiceDate is not { } date ? null
        : await _db.Reports.AsNoTracking()
            .Where(r => r.PropertyId == asset.PropertyId && r.InspectedDate == date && r.Technician != null)
            .OrderByDescending(r => r.IssuedDate)
            .Select(r => r.Technician)
            .FirstOrDefaultAsync(ct);

    public async Task<AssetEditVm?> EditFormAsync(int id, CancellationToken ct = default)
    {
        var asset = await WithDetails().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset == null) return null;

        var form = new AssetEditVm
        {
            Id = asset.Id,
            AssetTypeId = asset.AssetTypes.FirstOrDefault()?.Id,
            AssetTypeVariantId = asset.AssetTypeVariantId,
            Location = asset.Location,
            Label = asset.Name,
            Active = asset.IsActive,
            Ref = asset.Ref,
            Serial = asset.Serial,
            Barcode = asset.Barcode,
            BaseDate = asset.BaseDate,
            InstallationDate = asset.InstallationDate,
            InspectionOrder = asset.InspectionOrder,
            InternalNote = asset.InternalNote,
            Make = asset.Make,
            Model = asset.Model,
            Size = asset.Size,
            Quantity = asset.Quantity
        };
        await FillEditFormAsync(form, ct);
        return form;
    }

    public async Task FillEditFormAsync(AssetEditVm form, CancellationToken ct = default)
    {
        form.Asset ??= await WithDetails().AsNoTracking().FirstOrDefaultAsync(a => a.Id == form.Id, ct);
        var currentTypeIds = form.Asset?.AssetTypes.Select(t => t.Id).ToList() ?? new List<int>();

        // Active types, plus whatever the asset already is
        form.AssetTypes = (await _db.AssetTypes.AsNoTracking()
                .Where(t => t.Active || currentTypeIds.Contains(t.Id))
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(ct))
            .Select(t => (t.Id, t.Name)).ToList();
        form.Variants = (await _db.AssetTypeVariants.AsNoTracking()
                .Where(v => v.Active || v.Id == form.AssetTypeVariantId)
                .OrderBy(v => v.Name)
                .Select(v => new { v.Id, v.AssetTypeId, v.Name })
                .ToListAsync(ct))
            .Select(v => (v.Id, v.AssetTypeId, v.Name)).ToList();
    }

    public async Task<bool> SaveAsync(AssetEditVm form, string? author, CancellationToken ct = default)
    {
        var asset = await _db.Assets.Include(a => a.AssetTypes).FirstOrDefaultAsync(a => a.Id == form.Id, ct);
        if (asset == null) return false;

        var before = TimelineLog.AssetFields(asset);

        var type = form.AssetTypeId == null ? null : await _db.AssetTypes.FirstOrDefaultAsync(t => t.Id == form.AssetTypeId, ct);
        // A variant belongs to its type; changing the type resets it, as in Uptick
        var variant = form.AssetTypeVariantId == null ? null
            : await _db.AssetTypeVariants.FirstOrDefaultAsync(v => v.Id == form.AssetTypeVariantId && v.AssetTypeId == form.AssetTypeId, ct);

        var typeChanged = type?.Id != asset.AssetTypes.FirstOrDefault()?.Id;
        if (typeChanged)
        {
            asset.AssetTypes.Clear();
            if (type != null) asset.AssetTypes.Add(type);
        }

        // An imported variant name with no matching variant record is kept unless the type changes
        // (the form can't offer it, so leaving the list on "-" isn't a choice to clear it)
        if (variant != null || typeChanged || asset.AssetTypeVariantId != null)
        {
            asset.AssetTypeVariantId = variant?.Id;
            asset.Variant = variant?.Name;
        }
        asset.Location = Blank(form.Location);
        var autoLabel = string.Join(" ", new[] { variant?.Name, type?.Name }.Where(s => s != null));
        asset.Name = Blank(form.Label) ?? Blank(autoLabel) ?? asset.Name;
        if (asset.IsActive && !form.Active) asset.InactiveDate = DateTime.UtcNow;
        if (form.Active) asset.InactiveDate = null;
        asset.IsActive = form.Active;
        asset.Ref = Blank(form.Ref);
        asset.Serial = Blank(form.Serial);
        asset.Barcode = Blank(form.Barcode);
        asset.BaseDate = Day(form.BaseDate);
        asset.InstallationDate = Day(form.InstallationDate);
        asset.InspectionOrder = form.InspectionOrder;
        asset.InternalNote = Blank(form.InternalNote);
        asset.Make = Blank(form.Make);
        asset.Model = Blank(form.Model);
        asset.Size = Blank(form.Size);
        asset.Quantity = Blank(form.Quantity);

        var changes = TimelineLog.Changes(before, TimelineLog.AssetFields(asset), author, DateTime.UtcNow);
        foreach (var change in changes) change.AssetId = asset.Id;
        _db.TimelineEvents.AddRange(changes);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Calendar dates are stored as UTC midnight
    private static DateTime? Day(DateTime? d) => d == null ? null : DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc);
}
