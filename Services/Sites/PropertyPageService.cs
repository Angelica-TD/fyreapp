using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Routines;
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
    bool ShowClosedRemarks = false);

// Loads only what the open tab needs, as Uptick's tabs are separate pages
public class PropertyPageService : IPropertyPageService
{
    private const int TimelineSize = 100;

    private readonly AppDbContext _db;
    private readonly IRoutineService _routines;

    public PropertyPageService(AppDbContext db, IRoutineService routines)
    {
        _db = db;
        _routines = routines;
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
                Timeline = await TimelineAsync(site, ct)
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

    // What happened at the property, from FyreApp's records: tasks created and completed, reports issued,
    // remarks raised and closed. (Uptick's correspondence and invoices aren't in FyreApp.)
    private async Task<List<TimelineItemVm>> TimelineAsync(Site site, CancellationToken ct)
    {
        var items = new List<TimelineItemVm>();

        var tasks = await _db.ClientTasks.AsNoTracking()
            .Where(t => t.SiteId == site.Id)
            .OrderByDescending(t => t.CreatedUtc).Take(TimelineSize)
            .Select(t => new { t.Id, t.Ref, t.FyreRef, t.Title, t.CreatedUtc, t.CompletedUtc, t.Status })
            .ToListAsync(ct);
        foreach (var t in tasks)
        {
            var name = $"{t.Ref ?? t.FyreRef}: {t.Title}";
            items.Add(new TimelineItemVm
            {
                WhenUtc = t.CreatedUtc, Kind = "Task", Icon = "bi-list-check",
                Before = "Task ", LinkText = name, LinkController = "Task", LinkId = t.Id, After = " was created."
            });
            if (t.CompletedUtc is DateTime done && t.Status == ClientTaskStatus.Completed)
                items.Add(new TimelineItemVm
                {
                    WhenUtc = done, Kind = "Task", Icon = "bi-check2-circle",
                    Before = "Task ", LinkText = name, LinkController = "Task", LinkId = t.Id, After = " was completed."
                });
        }

        var reports = await _db.ServiceReports.AsNoTracking()
            .Where(r => r.SiteId == site.Id && (r.IssuedDate != null || r.InspectedDate != null))
            .OrderByDescending(r => r.IssuedDate ?? r.InspectedDate).Take(TimelineSize)
            .Select(r => new { r.Id, r.Ref, r.ExternalId, r.FyreRef, r.ReportType, r.IssuedDate, r.InspectedDate, r.ClientTaskId, r.TaskRef, r.TaskName })
            .ToListAsync(ct);
        foreach (var r in reports)
        {
            var report = $"{r.ReportType ?? "Report"} ({r.Ref ?? r.ExternalId ?? r.FyreRef})";
            items.Add(new TimelineItemVm
            {
                WhenUtc = (r.IssuedDate ?? r.InspectedDate)!.Value, DateOnly = true, Kind = "Service report", Icon = "bi-file-earmark-check",
                Before = $"{(r.IssuedDate != null ? "Issued" : "Inspected")} {report}" + (r.ClientTaskId != null ? " for task " : "."),
                LinkText = r.ClientTaskId != null ? $"{r.TaskRef}: {r.TaskName}" : null,
                LinkController = "Task", LinkId = r.ClientTaskId,
                After = r.ClientTaskId != null ? "." : null
            });
        }

        var remarks = await _db.Defects.AsNoTracking()
            .Where(d => d.SiteId == site.Id && d.RaisedUtc != null)
            .OrderByDescending(d => d.RaisedUtc).Take(TimelineSize)
            .Select(d => new { d.ExternalId, d.FyreRef, d.RemarkType, d.SeverityLabel, d.Description, d.RaisedUtc, d.InactiveUtc, d.Active })
            .ToListAsync(ct);
        foreach (var d in remarks)
        {
            var remark = $"{d.SeverityLabel ?? d.RemarkType ?? "Remark"} {(d.ExternalId != null ? $"D-{d.ExternalId}" : d.FyreRef)}";
            items.Add(new TimelineItemVm
            {
                WhenUtc = d.RaisedUtc!.Value, Kind = "Remark", Icon = "bi-exclamation-triangle",
                Before = $"{remark} raised: {Short(d.Description)}"
            });
            if (!d.Active && d.InactiveUtc is DateTime closed)
                items.Add(new TimelineItemVm
                {
                    WhenUtc = closed, Kind = "Remark", Icon = "bi-check2-square",
                    Before = $"{remark} was closed."
                });
        }

        if (site.Created is DateTime created)
            items.Add(new TimelineItemVm { WhenUtc = created, Kind = "Property", Icon = "bi-building", Before = "Property was created." });

        return items.OrderByDescending(i => i.WhenUtc).Take(TimelineSize).ToList();
    }

    private static string Short(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Length <= 120 ? s.Trim() : s[..117].Trim() + "…";
}
