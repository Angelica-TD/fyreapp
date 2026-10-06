using FyreApp.Data;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IListService
{
    Task<PropertyListVm> PropertiesAsync(PropertyFilter filter, CancellationToken ct = default);
    Task<RemarkListVm> RemarksAsync(RemarkFilter filter, CancellationToken ct = default);
    Task<ReportListVm> ReportsAsync(ReportFilter filter, CancellationToken ct = default);
}

// Properties, remarks (defects) and reports listed and filtered like Uptick's list pages
public class ListService : IListService
{
    private readonly AppDbContext _db;
    public ListService(AppDbContext db) => _db = db;

    public async Task<PropertyListVm> PropertiesAsync(PropertyFilter filter, CancellationToken ct = default)
    {
        var q = _db.Sites.AsNoTracking().AsQueryable();

        if (filter.Status.Count > 0)
        {
            var statuses = filter.Status.Select(s => s.ToUpperInvariant()).ToList();
            q = q.Where(s => s.Status != null && statuses.Contains(s.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.ExternalId != null && s.ExternalId.ToLower().Contains(term)) ||
                (s.AddressDisplay != null && s.AddressDisplay.ToLower().Contains(term)) ||
                s.Client.Name.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);

        // Newest first, as Uptick lists them
        var items = await q
            .OrderBy(s => s.Created == null)
            .ThenByDescending(s => s.Created)
            .ThenByDescending(s => s.Id)
            .Skip((filter.Page - 1) * ListFilters.PageSize)
            .Take(ListFilters.PageSize)
            .Select(s => new PropertyListItemVm
            {
                Id = s.Id,
                Ref = s.ExternalId ?? s.FyreRef,
                Status = s.Status,
                Created = s.Created,
                Name = s.Name,
                State = s.State,
                ClientId = s.ClientId,
                ClientName = s.Client.Name,
                ClientContact = s.Client.PrimaryContactName
            })
            .ToListAsync(ct);

        return new PropertyListVm { Filter = filter, Items = items, Total = total };
    }

    public async Task<RemarkListVm> RemarksAsync(RemarkFilter filter, CancellationToken ct = default)
    {
        var q = _db.Defects.AsNoTracking().AsQueryable();

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

        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);

        // Newest first: made in FyreApp, then Uptick IDs numerically
        var page = await q
            .OrderBy(d => d.ExternalId != null)
            .ThenByDescending(d => d.ExternalId == null ? d.Id : 0)
            .ThenByDescending(d => (d.ExternalId ?? "").Length)
            .ThenByDescending(d => d.ExternalId)
            .Skip((filter.Page - 1) * ListFilters.PageSize)
            .Take(ListFilters.PageSize)
            .Select(d => new
            {
                d.ExternalId, d.FyreRef, d.SeverityLabel, d.RemarkType, d.AssetId,
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
                Ref = string.IsNullOrWhiteSpace(d.ExternalId) ? d.FyreRef : $"D-{d.ExternalId}",
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

    public async Task<ReportListVm> ReportsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var q = _db.ServiceReports.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(r =>
                (r.Ref != null && r.Ref.ToLower().Contains(term)) ||
                (r.ReportType != null && r.ReportType.ToLower().Contains(term)) ||
                (r.TaskRef != null && r.TaskRef.ToLower().Contains(term)) ||
                r.Site.Name.ToLower().Contains(term) ||
                (r.Site.ExternalId != null && r.Site.ExternalId.ToLower().Contains(term)) ||
                r.Site.Client.Name.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);

        var items = await q
            .OrderBy(r => r.ExternalId != null)
            .ThenByDescending(r => r.ExternalId == null ? r.Id : 0)
            .ThenByDescending(r => (r.ExternalId ?? "").Length)
            .ThenByDescending(r => r.ExternalId)
            .Skip((filter.Page - 1) * ListFilters.PageSize)
            .Take(ListFilters.PageSize)
            .Select(r => new ReportListItemVm
            {
                Ref = r.Ref ?? r.ExternalId ?? r.FyreRef,
                Title = r.ReportType,
                Compliant = r.Compliant,
                Issued = r.IssuedDate,
                SiteId = r.SiteId,
                PropertyRef = r.Site.ExternalId ?? r.Site.FyreRef,
                PropertyName = r.Site.Name,
                ClientId = r.Site.ClientId,
                ClientName = r.Site.Client.Name,
                ClientContact = r.Site.Client.PrimaryContactName,
                TaskRef = r.TaskRef
            })
            .ToListAsync(ct);

        return new ReportListVm { Filter = filter, Items = items, Total = total };
    }
}
