using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Uptick.Files;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface ICorrespondenceListService
{
    Task<CorrespondenceListVm> SearchAsync(CorrespondenceFilter filter, CancellationToken ct = default);

    // An email with the records it's about and its attachments, or null
    Task<CorrespondencePageVm?> DetailsAsync(int id, CancellationToken ct = default);

    // A record's latest emails and its dispatches, for its page
    Task<CorrespondencePanelVm> PanelAsync(string record, long uptickId, int take, CancellationToken ct = default);
}

// Uptick's emails, newest first
public partial class CorrespondenceListService : ICorrespondenceListService
{
    // What each kind of record is called on a page
    public static readonly Dictionary<string, string> Kinds = new()
    {
        ["clients"] = "Client", ["properties"] = "Property", ["tasks"] = "Task", ["invoices"] = "Invoice",
        ["defectquotes"] = "Quote", ["servicequotes"] = "Sales quote", ["contractors"] = "Contractor", ["users"] = "User",
        ["dispatches"] = "Dispatch", ["dynamicformresponses"] = "Form"
    };

    private readonly AppDbContext _db;
    public CorrespondenceListService(AppDbContext db) => _db = db;

    private IQueryable<Correspondence> Filtered(CorrespondenceFilter filter)
    {
        var q = _db.Correspondence.AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Record) && filter.RecordId is { } recordId)
            q = q.Where(c => c.Links.Any(l => l.RecordType == filter.Record && l.RecordUptickId == recordId));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(c =>
                (c.Subject != null && c.Subject.ToLower().Contains(term)) ||
                (c.From != null && c.From.ToLower().Contains(term)) ||
                (c.To != null && c.To.ToLower().Contains(term)) ||
                (c.Cc != null && c.Cc.ToLower().Contains(term)));
        }
        return q.OrderByDescending(c => c.SentUtc).ThenByDescending(c => c.UptickId);
    }

    // The list's rows (attachments are a jsonb column, counted once read)
    private static async Task<List<CorrespondenceListItemVm>> RowsAsync(IQueryable<Correspondence> q, CancellationToken ct) =>
        (await q.Select(c => new { c.Id, c.Subject, From = c.FromName ?? c.From, c.To, c.SentUtc, c.Attachments }).ToListAsync(ct))
        .Select(c => new CorrespondenceListItemVm
        {
            Id = c.Id, Subject = c.Subject, From = c.From, To = c.To, SentUtc = c.SentUtc, Attachments = c.Attachments.Count
        })
        .ToList();

    public async Task<CorrespondenceListVm> SearchAsync(CorrespondenceFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();
        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);
        var items = await RowsAsync(q.Skip((filter.Page - 1) * ListFilters.PageSize).Take(ListFilters.PageSize), ct);

        RecordLinkVm? record = null;
        if (!string.IsNullOrWhiteSpace(filter.Record) && filter.RecordId is { } id)
        {
            var name = await _db.CorrespondenceLinks.AsNoTracking()
                .Where(l => l.RecordType == filter.Record && l.RecordUptickId == id && l.Name != null)
                .Select(l => l.Name).FirstOrDefaultAsync(ct);
            record = (await RecordsAsync([(filter.Record, id, name)], ct)).FirstOrDefault();
        }
        return new CorrespondenceListVm { Filter = filter, Items = items, Total = total, RecordLink = record };
    }

    public async Task<CorrespondencePageVm?> DetailsAsync(int id, CancellationToken ct = default)
    {
        var email = await _db.Correspondence.AsNoTracking()
            .Include(c => c.SentBy)
            .Include(c => c.Links)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (email == null) return null;

        var dispatchId = email.Links.FirstOrDefault(l => l.RecordType == "dispatches")?.RecordUptickId;
        var dispatch = dispatchId == null ? null
            : await _db.Dispatches.AsNoTracking().Include(d => d.Sender).FirstOrDefaultAsync(d => d.UptickId == dispatchId, ct);

        return new CorrespondencePageVm
        {
            Email = email,
            Records = await RecordsAsync(email.Links.OrderBy(l => l.Id).Select(l => (l.RecordType, l.RecordUptickId, l.Name)), ct),
            Attachments = await AttachmentsAsync(email.Attachments, ct),
            Dispatch = dispatch
        };
    }

    public async Task<CorrespondencePanelVm> PanelAsync(string record, long uptickId, int take, CancellationToken ct = default)
    {
        var q = Filtered(new CorrespondenceFilter { Record = record, RecordId = uptickId }).AsNoTracking();
        var total = await q.CountAsync(ct);
        var latest = total == 0 ? [] : await RowsAsync(q.Take(take), ct);

        // A task's dispatches (a property's emails already list each one sent)
        var rows = new List<DispatchRowVm>();
        if (record == "tasks")
        {
            var found = await _db.Dispatches.AsNoTracking().Include(d => d.Sender).Include(d => d.ClientTask)
                .Where(d => d.ClientTask != null && d.ClientTask.UptickId == uptickId)
                .OrderByDescending(d => d.CreatedUtc).ThenByDescending(d => d.UptickId).ToListAsync(ct);
            var ids = found.Select(d => d.UptickId).OfType<long>().ToList();
            var emails = (await _db.CorrespondenceLinks.AsNoTracking()
                    .Where(l => l.RecordType == "dispatches" && ids.Contains(l.RecordUptickId))
                    .Select(l => new { l.RecordUptickId, l.CorrespondenceId }).ToListAsync(ct))
                .GroupBy(l => l.RecordUptickId).ToDictionary(g => g.Key, g => g.Min(l => l.CorrespondenceId));
            var attachments = await AttachmentsAsync(found.SelectMany(d => d.Emails).SelectMany(e => e.Attachments), ct);
            var byName = attachments.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First());
            foreach (var d in found)
                rows.Add(new DispatchRowVm(d, d.UptickId is { } u && emails.TryGetValue(u, out var e) ? e : null,
                    d.Emails.SelectMany(m => m.Attachments).Select(a => a.Name).Distinct()
                        .Select(n => byName.TryGetValue(n, out var a) ? a : new AttachmentVm(n, null, null)).ToList()));
        }

        return new CorrespondencePanelVm { Record = record, RecordId = uptickId, Latest = latest, Total = total, Dispatches = rows };
    }

    // The records, with FyreApp's page for those FyreApp has (clients, properties, tasks)
    private async Task<List<RecordLinkVm>> RecordsAsync(IEnumerable<(string Type, long UptickId, string? Name)> records, CancellationToken ct)
    {
        var list = records.DistinctBy(r => (r.Type, r.UptickId)).ToList();
        List<long> Of(string type) => list.Where(r => r.Type == type).Select(r => r.UptickId).ToList();
        var (clientIds, propertyIds, taskIds) = (Of("clients"), Of("properties"), Of("tasks"));
        var clients = clientIds.Count == 0 ? new Dictionary<long, int>()
            : (await _db.Clients.AsNoTracking().Where(c => c.UptickId != null && clientIds.Contains(c.UptickId.Value))
                .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct)).ToDictionary(c => c.UptickId, c => c.Id);
        var properties = propertyIds.Count == 0 ? new Dictionary<long, int>()
            : (await _db.Properties.AsNoTracking().Where(p => p.UptickId != null && propertyIds.Contains(p.UptickId.Value))
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct)).ToDictionary(p => p.UptickId, p => p.Id);
        var tasks = taskIds.Count == 0 ? new Dictionary<long, int>()
            : (await _db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null && taskIds.Contains(t.UptickId.Value))
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct)).ToDictionary(t => t.UptickId, t => t.Id);

        return list.Select(r =>
        {
            var kind = Kinds.TryGetValue(r.Type, out var k) ? k : r.Type;
            var name = r.Name ?? $"{kind} {r.UptickId}";
            var (controller, map) = r.Type switch
            {
                "clients" => ("Clients", clients),
                "properties" => ("Property", properties),
                "tasks" => ("Task", tasks),
                _ => ((string?)null, (Dictionary<long, int>?)null)
            };
            return map != null && map.TryGetValue(r.UptickId, out var id)
                ? new RecordLinkVm(kind, name, controller, id)
                : new RecordLinkVm(kind, name, null, null);
        }).ToList();
    }

    [GeneratedRegex(@"^reports/(\d+)/")]
    private static partial Regex ReportPath();

    // Each attachment as FyreApp holds it: a report's page and PDF ("reports/<id>/…"), or the document file at its path.
    // Invoices' and quotes' PDFs aren't copied yet (docs/uptick-migration.md §8).
    private async Task<List<AttachmentVm>> AttachmentsAsync(IEnumerable<EmailAttachment> attachments, CancellationToken ct)
    {
        var list = attachments.ToList();
        var reportIds = list.Select(a => a.Path is { } p && ReportPath().Match(p) is { Success: true } m ? long.Parse(m.Groups[1].Value) : (long?)null)
            .OfType<long>().Distinct().ToList();
        var reports = reportIds.Count == 0 ? new Dictionary<long, int>()
            : (await _db.Reports.AsNoTracking().Where(r => r.UptickId != null && reportIds.Contains(r.UptickId.Value))
                .Select(r => new { UptickId = r.UptickId!.Value, r.Id }).ToListAsync(ct)).ToDictionary(r => r.UptickId, r => r.Id);
        var pdfs = await UptickFileLinks.PdfsAsync(_db, UptickResources.Reports, reportIds.Select(i => (long?)i), ct);
        var paths = list.Select(a => a.Path).OfType<string>().Where(p => !ReportPath().IsMatch(p)).Distinct().ToList();
        var files = paths.Count == 0 ? new Dictionary<string, int>()
            : (await _db.UptickFiles.AsNoTracking().Where(f => f.Path != null && paths.Contains(f.Path) && f.StorageKey != null)
                .Select(f => new { f.Path, f.Id }).ToListAsync(ct)).GroupBy(f => f.Path!).ToDictionary(g => g.Key, g => g.First().Id);

        return list.Select(a =>
        {
            if (a.Path is { } p && ReportPath().Match(p) is { Success: true } m && long.TryParse(m.Groups[1].Value, out var report))
                return new AttachmentVm(a.Name, reports.TryGetValue(report, out var r) ? r : null, pdfs.TryGetValue(report, out var f) ? f : null);
            return new AttachmentVm(a.Name, null, a.Path != null && files.TryGetValue(a.Path, out var file) ? file : null);
        }).ToList();
    }
}
