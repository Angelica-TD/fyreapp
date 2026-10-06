using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IReportListService
{
    Task<ReportListVm> SearchAsync(ReportFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(ReportFilter filter, CancellationToken ct = default);
}

// Service reports listed like Uptick's Reports page (no default filters, no Edit)
public class ReportListService : IReportListService
{
    private readonly AppDbContext _db;
    public ReportListService(AppDbContext db) => _db = db;

    private IQueryable<ServiceReport> Filtered(ReportFilter filter)
    {
        var q = _db.ServiceReports.AsQueryable();

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

        // Newest first: made in FyreApp, then Uptick IDs numerically
        return q
            .OrderBy(r => r.ExternalId != null)
            .ThenByDescending(r => r.ExternalId == null ? r.Id : 0)
            .ThenByDescending(r => (r.ExternalId ?? "").Length)
            .ThenByDescending(r => r.ExternalId);
    }

    public async Task<ReportListVm> SearchAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();
        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);

        var items = await q
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

    public async Task<byte[]> CsvAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(r => new
            {
                Ref = r.Ref ?? r.ExternalId ?? r.FyreRef, r.ReportType, r.Compliant, r.IssuedDate, r.InspectedDate,
                PropertyRef = r.Site.ExternalId ?? r.Site.FyreRef, Property = r.Site.Name, Client = r.Site.Client.Name,
                r.TaskRef, r.TaskName, r.Technician, r.Published
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Ref", "Title", "Status", "Issued", "Inspected", "Property ref", "Property", "Client", "Task ref", "Task", "Technician", "Published" },
            rows.Select(r => new[]
            {
                r.Ref, r.ReportType, r.Compliant switch { true => "Pass", false => "Fail", _ => null },
                CsvExport.Date(r.IssuedDate), CsvExport.Date(r.InspectedDate), r.PropertyRef, r.Property, r.Client,
                r.TaskRef, r.TaskName, r.Technician, r.Published ? "Yes" : "No"
            }));
    }
}
