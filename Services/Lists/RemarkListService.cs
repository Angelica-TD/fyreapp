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

    // One remark with its property, client, asset, remark type and repair task, for its details page
    Task<Remark?> FindAsync(int id, CancellationToken ct = default);

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

    public Task<Remark?> FindAsync(int id, CancellationToken ct = default) =>
        _db.Remarks.AsNoTracking()
            .Include(d => d.Property).ThenInclude(s => s.Client)
            .Include(d => d.Asset).ThenInclude(a => a!.AssetTypes)
            .Include(d => d.RemarkTypeRef)
            .Include(d => d.RepairTask)
            .Include(d => d.RaisedOnTask)
            .Include(d => d.DefectQuote)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<Remark> Filtered(RemarkFilter filter)
    {
        var q = _db.Remarks.AsQueryable();

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
            q = q.Where(d => d.Property.Status != null && statuses.Contains(d.Property.Status));
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
                (d.UptickId != null && d.UptickId.ToString().Contains(refTerm)) ||
                (d.RemarkType != null && d.RemarkType.ToLower().Contains(term)) ||
                (d.Description != null && d.Description.ToLower().Contains(term)) ||
                (d.Asset != null && d.Asset.Name.ToLower().Contains(term)) ||
                d.Property.Name.ToLower().Contains(term) ||
                (d.Property.Ref != null && d.Property.Ref.ToLower().Contains(term)));
        }

        // Newest first: made in FyreApp, then Uptick IDs numerically
        return q
            .OrderBy(d => d.UptickId != null)
            .ThenByDescending(d => d.UptickId == null ? d.Id : 0)
            .ThenByDescending(d => d.UptickId);
    }

    private IQueryable<Remark> Selected(BulkSelection selection, RemarkFilter filter)
    {
        if (selection.AllMatching) return Filtered(filter);
        var ids = selection.Ids.ToList();
        return _db.Remarks.Where(d => ids.Contains(d.Id));
    }

    private static string? RefOf(long? uptickId, string? fyreRef) =>
        uptickId is { } id ? $"D-{id}" : fyreRef;

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
                d.Id, d.UptickId, d.FyreRef, d.SeverityLabel, d.RemarkType, d.AssetId,
                AssetName = d.Asset != null ? d.Asset.Name : null,
                d.PropertyId, PropertyRef = d.Property.Ref ?? d.Property.FyreRef, PropertyName = d.Property.Name,
                d.QuoteStatus, d.QuoteRef
            })
            .ToListAsync(ct);

        var severityOptions = await _db.Remarks.AsNoTracking()
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
                Ref = RefOf(d.UptickId, d.FyreRef),
                Severity = d.SeverityLabel,
                RemarkType = d.RemarkType,
                AssetId = d.AssetId,
                AssetName = d.AssetName,
                PropertyId = d.PropertyId,
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
                d.UptickId, d.FyreRef, d.SeverityLabel, d.RemarkType, d.Status, d.Active, d.Description, d.Resolution, d.Location,
                Asset = d.Asset != null ? d.Asset.Name : null,
                PropertyRef = d.Property.Ref ?? d.Property.FyreRef, Property = d.Property.Name, Client = d.Property.Client.Name,
                d.RaisedUtc, d.QuoteRef, d.QuoteStatus, d.RepairTaskRef
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Ref", "Severity", "Remark type", "Status", "Compliance", "Description", "Resolution", "Location", "Asset",
                    "Property ref", "Property", "Client", "Raised", "Quote ref", "Quote status", "Repair task" },
            rows.Select(r => new[]
            {
                RefOf(r.UptickId, r.FyreRef), r.SeverityLabel, r.RemarkType, r.Status, r.Active ? "Open" : "Resolved",
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
            .Include(d => d.Property)
            .Include(d => d.Asset)
            .ToListAsync(ct);

        input.Category ??= "Repair";
        var tasks = BulkTaskBuilder.PerProperty(
            defects, d => d.Property,
            d => $"{RefOf(d.UptickId, d.FyreRef)} {d.RemarkType}{(d.Asset != null ? $" ({d.Asset.Name})" : "")}{(string.IsNullOrWhiteSpace(d.Description) ? "" : $": {d.Description}")}",
            input, "Defect repairs", userId);

        _db.ClientTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);

        // Each defect records its repair task (FyreApp ref, assigned by the database on insert)
        var taskBySite = tasks.ToDictionary(t => t.PropertyId);
        foreach (var d in defects)
        {
            var task = taskBySite[d.PropertyId];
            d.RepairTaskRef = task.DisplayRef;
            d.RepairTaskStatus = "Open";
        }
        await _db.SaveChangesAsync(ct);

        return tasks.Count;
    }
}
