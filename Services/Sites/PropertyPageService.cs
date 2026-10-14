using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.Services.Timeline;
using FyreApp.ViewModels.Sites;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Sites;

public interface IPropertyPageService
{
    // The property page with what its tab shows; null when there's no such property
    Task<PropertyPageVm?> LoadAsync(int id, PropertyPageQuery query, CancellationToken ct = default);
}

// What the page was asked for: the tab, plus that tab's filters
public record PropertyPageQuery(
    string? Tab, int? Year = null, string? AssetActive = null, int? AssetType = null, string? AssetSearch = null,
    bool ShowClosedRemarks = false, bool NotesOnly = false);

// Loads only what the open tab needs, as Uptick's tabs are separate pages
public class PropertyPageService : IPropertyPageService
{
    private readonly AppDbContext _db;
    private readonly IRoutineService _routines;
    private readonly ITimelineService _timeline;

    public PropertyPageService(AppDbContext db, IRoutineService routines, ITimelineService timeline)
    {
        _db = db;
        _routines = routines;
        _timeline = timeline;
    }

    public async Task<PropertyPageVm?> LoadAsync(int id, PropertyPageQuery query, CancellationToken ct = default)
    {
        var tab = PropertyPageVm.NormaliseTab(query.Tab);

        var site = await _db.Sites.AsNoTracking()
            .Include(s => s.Client).ThenInclude(c => c.Contacts)
            .Include(s => s.Contacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site == null) return null;

        // Uptick's severity: higher is worse (10 critical defect … 0 informational)
        var openRemarks = _db.Defects.AsNoTracking().Where(d => d.SiteId == id && d.Active);
        var openCount = await openRemarks.CountAsync(ct);
        var mostSevere = await openRemarks.OrderByDescending(d => d.Severity ?? -1).FirstOrDefaultAsync(ct);

        return tab switch
        {
            "assets" => await AssetsAsync(site, query, openCount, mostSevere, ct),
            "remarks" => new PropertyPageVm
            {
                Site = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                ShowClosedRemarks = query.ShowClosedRemarks,
                Remarks = await _db.Defects.AsNoTracking()
                    .Include(d => d.Asset)
                    .Where(d => d.SiteId == id && (query.ShowClosedRemarks || d.Active))
                    .OrderByDescending(d => d.Severity ?? -1).ThenByDescending(d => d.RaisedUtc)
                    .ToListAsync(ct)
            },
            "routines" => new PropertyPageVm
            {
                Site = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Routines = await _routines.ForPropertyAsync(id, Math.Clamp(query.Year ?? DateTime.UtcNow.ToSydney().Year, 2000, 2100), ct)
            },
            "records" => new PropertyPageVm
            {
                Site = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Reports = await _db.ServiceReports.AsNoTracking()
                    .Where(r => r.SiteId == id)
                    .OrderByDescending(r => r.IssuedDate ?? r.InspectedDate)
                    .ToListAsync(ct)
            },
            "tasks" => new PropertyPageVm
            {
                Site = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Tasks = await _db.ClientTasks.AsNoTracking()
                    .Include(t => t.AssignedTo)
                    .Where(t => t.SiteId == id)
                    // Open work first, then the latest due
                    .OrderBy(t => t.Status == ClientTaskStatus.Completed || t.Status == ClientTaskStatus.Cancelled)
                    .ThenByDescending(t => t.DueDateUtc)
                    .ToListAsync(ct)
            },
            "billing" => new PropertyPageVm { Site = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere },
            _ => new PropertyPageVm
            {
                Site = site, Tab = tab, OpenRemarks = openCount, MostSevereRemark = mostSevere,
                Timeline = await _timeline.ForPropertyAsync(site, query.NotesOnly, ct),
                NotesOnly = query.NotesOnly,
                ExtraFields = await FyreApp.Services.Imports.UptickReferenceValues.ExtraFieldsAsync(_db, ExtraField.PropertyContentType, site.UptickData, ct)
            }
        };
    }

    private async Task<PropertyPageVm> AssetsAsync(Site site, PropertyPageQuery query, int openCount, Defect? mostSevere, CancellationToken ct)
    {
        var all = await _db.Assets.AsNoTracking()
            .Include(a => a.AssetTypes)
            .Where(a => a.SiteId == site.Id)
            .ToListAsync(ct);

        // Uptick's default: active assets, any type
        var active = query.AssetActive ?? "yes";
        var search = query.AssetSearch?.Trim();
        var shown = all
            .Where(a => active switch { "yes" => a.IsActive, "no" => !a.IsActive, _ => true })
            .Where(a => query.AssetType == null || a.AssetTypes.Any(t => t.Id == query.AssetType))
            .Where(a => string.IsNullOrEmpty(search) ||
                        new[] { a.Name, a.Ref, a.Location, a.Variant, a.Barcode }
                            .Any(v => v != null && v.Contains(search, StringComparison.OrdinalIgnoreCase)))
            // Uptick's inspection order, then by label
            .OrderBy(a => a.InspectionOrder ?? int.MaxValue).ThenBy(a => a.Name)
            .ToList();

        return new PropertyPageVm
        {
            Site = site, Tab = "assets", OpenRemarks = openCount, MostSevereRemark = mostSevere,
            Assets = shown,
            AssetTypes = all.SelectMany(a => a.AssetTypes).DistinctBy(t => t.Id).OrderBy(t => t.Name).Select(t => (t.Id, t.Name)).ToList(),
            AssetActive = active,
            AssetTypeId = query.AssetType,
            AssetSearch = search
        };
    }

}
