using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Uptick.Files;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IReportListService
{
    Task<ReportListVm> SearchAsync(ReportFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(ReportFilter filter, CancellationToken ct = default);

    // A report with its lines (each asset on it) and its PDF, or null
    Task<ReportPageVm?> DetailsAsync(int id, CancellationToken ct = default);
}

// Service reports listed like Uptick's Reports page (no default filters, no Edit)
public class ReportListService : IReportListService
{
    private readonly AppDbContext _db;
    public ReportListService(AppDbContext db) => _db = db;

    public async Task<ReportPageVm?> DetailsAsync(int id, CancellationToken ct = default)
    {
        var report = await _db.Reports.AsNoTracking()
            .Include(r => r.Property).ThenInclude(p => p.Client)
            .Include(r => r.ClientTask)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report == null) return null;
        var items = await _db.ReportItems.AsNoTracking()
            .Include(i => i.Asset)
            .Where(i => i.ReportId == id)
            .ToListAsync(ct);
        var pdfs = await UptickFileLinks.PdfsAsync(_db, UptickResources.Reports, [report.UptickId], ct);
        return new ReportPageVm
        {
            Report = report,
            // As the report lists them: by ref ("001", "002", …), then label
            Items = items.OrderBy(i => i.Ref?.Length ?? int.MaxValue).ThenBy(i => i.Ref).ThenBy(i => i.Label).ToList(),
            PdfFileId = report.UptickId is { } uid && pdfs.TryGetValue(uid, out var file) ? file : null,
            Work = report.UptickId == null && report.ClientTaskId is { } taskId ? await WorkAsync(taskId, ct) : null
        };
    }

    // What was done on the task, for a report made in the field app
    private async Task<ReportWorkVm> WorkAsync(int taskId, CancellationToken ct)
    {
        static string? AssetLabel(Asset? a) => a == null ? null : string.IsNullOrWhiteSpace(a.Ref) ? a.Name : $"{a.Ref} - {a.Name}";
        static ReportRemark Line(Remark r) =>
            new(r.Id, r.DisplayRef, AssetLabel(r.Asset), r.RemarkType, r.SeverityLabel, r.Description, r.Resolution);

        var raised = await _db.Remarks.AsNoTracking().Include(r => r.Asset)
            .Where(r => r.RaisedOnTaskId == taskId)
            .OrderBy(r => r.RaisedUtc).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var resolved = await _db.Remarks.AsNoTracking().Include(r => r.Asset)
            .Where(r => !r.Active && _db.RemarkEvents.Any(e => e.RemarkId == r.Id && e.ClientTaskId == taskId && e.Event == "R"))
            .OrderBy(r => r.InactiveUtc).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var time = await _db.TaskSessions.AsNoTracking()
            .Where(s => s.ClientTaskId == taskId && s.TrashedUtc == null && s.Hours != null)
            .GroupBy(s => s.IsOnsite)
            .Select(g => new { OnSite = g.Key, Hours = g.Sum(s => s.Hours!.Value) })
            .ToListAsync(ct);

        return new ReportWorkVm
        {
            TaskId = taskId,
            Raised = raised.Select(Line).ToList(),
            Resolved = resolved.Select(Line).ToList(),
            Forms = (await _db.DynamicFormResponses.AsNoTracking()
                    .Where(f => f.ClientTaskId == taskId)
                    .OrderBy(f => f.SubmittedUtc)
                    .Select(f => new { f.Id, f.TemplateName, f.Ref, f.SubmittedUtc, By = f.UptickUser != null ? f.UptickUser.Name : null, f.IsComplete })
                    .ToListAsync(ct))
                .Select(f => new Services.Forms.FieldFormSummary(f.Id, f.TemplateName ?? "Form", f.Ref, f.SubmittedUtc, f.By, f.IsComplete))
                .ToList(),
            SignOff = await _db.TaskSignOffs.AsNoTracking()
                .Where(s => s.ClientTaskId == taskId)
                .OrderByDescending(s => s.SignedUtc)
                .FirstOrDefaultAsync(ct),
            OnSiteHours = time.Where(t => t.OnSite).Sum(t => t.Hours),
            TravelHours = time.Where(t => !t.OnSite).Sum(t => t.Hours)
        };
    }

    private IQueryable<Report> Filtered(ReportFilter filter)
    {
        var q = _db.Reports.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(r =>
                (r.Ref != null && r.Ref.ToLower().Contains(term)) ||
                (r.ReportType != null && r.ReportType.ToLower().Contains(term)) ||
                (r.TaskRef != null && r.TaskRef.ToLower().Contains(term)) ||
                r.Property.Name.ToLower().Contains(term) ||
                (r.Property.Ref != null && r.Property.Ref.ToLower().Contains(term)) ||
                r.Property.Client.Name.ToLower().Contains(term));
        }

        // Newest first: made in FyreApp, then Uptick IDs numerically
        return q
            .OrderBy(r => r.UptickId != null)
            .ThenByDescending(r => r.UptickId == null ? r.Id : 0)
            .ThenByDescending(r => r.UptickId);
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
                Ref = r.Ref ?? (r.UptickId != null ? r.UptickId.ToString() : r.FyreRef),
                Title = r.ReportType,
                Compliant = r.Compliant,
                Issued = r.IssuedDate,
                PropertyId = r.PropertyId,
                PropertyRef = r.Property.Ref ?? r.Property.FyreRef,
                PropertyName = r.Property.Name,
                ClientId = r.Property.ClientId,
                ClientName = r.Property.Client.Name,
                ClientContact = r.Property.Client.PrimaryContactName,
                TaskRef = r.TaskRef,
                Id = r.Id,
                UptickId = r.UptickId
            })
            .ToListAsync(ct);
        var pdfs = await UptickFileLinks.PdfsAsync(_db, UptickResources.Reports, items.Select(i => i.UptickId), ct);
        foreach (var i in items)
            if (i.UptickId is { } id && pdfs.TryGetValue(id, out var file)) i.PdfFileId = file;

        return new ReportListVm { Filter = filter, Items = items, Total = total };
    }

    public async Task<byte[]> CsvAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(r => new
            {
                Ref = r.Ref ?? (r.UptickId != null ? r.UptickId.ToString() : r.FyreRef), r.ReportType, r.Compliant, r.IssuedDate, r.InspectedDate,
                PropertyRef = r.Property.Ref ?? r.Property.FyreRef, Property = r.Property.Name, Client = r.Property.Client.Name,
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
