using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IRemarkListService
{
    Task<RemarkListVm> SearchAsync(RemarkFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(RemarkFilter filter, CancellationToken ct = default);

    // Edit → Mark resolved / Reopen (Uptick's remark "Active": open = not resolved)
    Task<int> SetResolvedAsync(BulkSelection selection, RemarkFilter filter, bool resolved, CancellationToken ct = default);

    // Edit → Generate repair tasks: one per property, listing its selected defects. Each defect records the task.
    Task<int> GenerateRepairTasksAsync(BulkSelection selection, RemarkFilter filter, BulkTaskInput input, string? userId, CancellationToken ct = default);
}

// Remarks (FyreApp defects) listed, filtered and edited like Uptick's Remarks page
public class RemarkListService : IRemarkListService
{
    private readonly AppDbContext _db;
    public RemarkListService(AppDbContext db) => _db = db;

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<Defect> Filtered(RemarkFilter filter)
    {
        var q = _db.Defects.AsQueryable();

        if (filter.AssetActive is bool assetActive)
            q = q.Where(d => d.Asset != null && d.Asset.IsActive == assetActive);

        // Open = not resolved (Uptick's remark "Active")
        var open = filter.Compliance.Contains(RemarkCompliance.Open);
        var resolved = filter.Compliance.Contains(RemarkCompliance.Resolved);
        if (open != resolved)
            q = q.Where(d => d.Active == open);

        if (filter.PropertyStatus.Count > 0)
        {
            var statuses = filter.PropertyStatus.Select(s => s.ToUpperInvariant()).ToList();
            q = q.Where(d => d.Site.Status != null && statuses.Contains(d.Site.Status));
        }

        if (filter.Severity.Count > 0)
        {
            var severities = filter.Severity.ToList();
            q = q.Where(d => d.SeverityLabel != null && severities.Contains(d.SeverityLabel));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            var refTerm = term.StartsWith("d-") ? term[2..] : term;
            q = q.Where(d =>
                (d.ExternalId != null && d.ExternalId.Contains(refTerm)) ||
                (d.RemarkType != null && d.RemarkType.ToLower().Contains(term)) ||
                (d.Description != null && d.Description.ToLower().Contains(term)) ||
                (d.Asset != null && d.Asset.Name.ToLower().Contains(term)) ||
                d.Site.Name.ToLower().Contains(term) ||
                (d.Site.ExternalId != null && d.Site.ExternalId.ToLower().Contains(term)));
        }

        // Newest first: made in FyreApp, then Uptick IDs numerically
        return q
            .OrderBy(d => d.ExternalId != null)
            .ThenByDescending(d => d.ExternalId == null ? d.Id : 0)
            .ThenByDescending(d => (d.ExternalId ?? "").Length)
            .ThenByDescending(d => d.ExternalId);
    }

    private IQueryable<Defect> Selected(BulkSelection selection, RemarkFilter filter)
    {
        if (selection.AllMatching) return Filtered(filter);
        var ids = selection.Ids.ToList();
        return _db.Defects.Where(d => ids.Contains(d.Id));
    }

    private static string? RefOf(string? externalId, string? fyreRef) =>
        string.IsNullOrWhiteSpace(externalId) ? fyreRef : $"D-{externalId}";

    public async Task<RemarkListVm> SearchAsync(RemarkFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();
        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);

        var page = await q
            .Skip((filter.Page - 1) * ListFilters.PageSize)
            .Take(ListFilters.PageSize)
            .Select(d => new
            {
                d.Id, d.ExternalId, d.FyreRef, d.SeverityLabel, d.RemarkType, d.AssetId,
                AssetName = d.Asset != null ? d.Asset.Name : null,
                d.SiteId, PropertyRef = d.Site.ExternalId ?? d.Site.FyreRef, PropertyName = d.Site.Name,
                d.QuoteStatus, d.QuoteRef
            })
            .ToListAsync(ct);

        var severityOptions = await _db.Defects.AsNoTracking()
            .Where(d => d.SeverityLabel != null)
            .Select(d => d.SeverityLabel!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        return new RemarkListVm
        {
            Filter = filter,
            Total = total,
            Severities = RemarkFilter.DefaultSeverities.Union(severityOptions).ToList(),
            Items = page.Select(d => new RemarkListItemVm
            {
                Id = d.Id,
                Ref = RefOf(d.ExternalId, d.FyreRef),
                Severity = d.SeverityLabel,
                RemarkType = d.RemarkType,
                AssetId = d.AssetId,
                AssetName = d.AssetName,
                SiteId = d.SiteId,
                PropertyRef = d.PropertyRef,
                PropertyName = d.PropertyName,
                QuoteStatus = d.QuoteStatus,
                QuoteRef = d.QuoteRef
            }).ToList()
        };
    }

    public async Task<byte[]> CsvAsync(RemarkFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(d => new
            {
                d.ExternalId, d.FyreRef, d.SeverityLabel, d.RemarkType, d.Status, d.Active, d.Description, d.Resolution, d.Location,
                Asset = d.Asset != null ? d.Asset.Name : null,
                PropertyRef = d.Site.ExternalId ?? d.Site.FyreRef, Property = d.Site.Name, Client = d.Site.Client.Name,
                d.RaisedUtc, d.QuoteRef, d.QuoteStatus, d.RepairTaskRef
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Ref", "Severity", "Remark type", "Status", "Compliance", "Description", "Resolution", "Location", "Asset",
                    "Property ref", "Property", "Client", "Raised", "Quote ref", "Quote status", "Repair task" },
            rows.Select(r => new[]
            {
                RefOf(r.ExternalId, r.FyreRef), r.SeverityLabel, r.RemarkType, r.Status, r.Active ? "Open" : "Resolved",
                r.Description, r.Resolution, r.Location, r.Asset, r.PropertyRef, r.Property, r.Client,
                CsvExport.Date(r.RaisedUtc?.ToSydney()), r.QuoteRef, r.QuoteStatus, r.RepairTaskRef
            }));
    }

    public async Task<int> SetResolvedAsync(BulkSelection selection, RemarkFilter filter, bool resolved, CancellationToken ct = default)
    {
        var defects = await Selected(selection, filter).ToListAsync(ct);
        foreach (var d in defects)
        {
            d.Active = !resolved;
            if (resolved) d.Status = "Resolved";
            else if (d.Status == "Resolved") d.Status = null;
        }
        await _db.SaveChangesAsync(ct);
        return defects.Count;
    }

    public async Task<int> GenerateRepairTasksAsync(BulkSelection selection, RemarkFilter filter, BulkTaskInput input, string? userId, CancellationToken ct = default)
    {
        var defects = await Selected(selection, filter)
            .Include(d => d.Site)
            .Include(d => d.Asset)
            .ToListAsync(ct);

        input.Category ??= "Repair";
        var tasks = BulkTaskBuilder.PerProperty(
            defects, d => d.Site,
            d => $"{RefOf(d.ExternalId, d.FyreRef)} {d.RemarkType}{(d.Asset != null ? $" ({d.Asset.Name})" : "")}{(string.IsNullOrWhiteSpace(d.Description) ? "" : $": {d.Description}")}",
            input, "Defect repairs", userId);

        _db.ClientTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);

        // Each defect records its repair task (FyreApp ref, assigned by the database on insert)
        var taskBySite = tasks.ToDictionary(t => t.SiteId);
        foreach (var d in defects)
        {
            var task = taskBySite[d.SiteId];
            d.RepairTaskRef = task.DisplayRef;
            d.RepairTaskStatus = "Open";
        }
        await _db.SaveChangesAsync(ct);

        return tasks.Count;
    }
}
