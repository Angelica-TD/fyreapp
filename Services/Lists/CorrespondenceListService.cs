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

    // The records FyreApp has a page for, by Uptick endpoint: the page, and the folder an attachment's path names one
    // by ("reports/<id>/…", "quotes/<id>/…": where Uptick keeps the record's PDF)
    private sealed record Page(string Resource, string Controller, string Action, string? Folder);

    private static readonly Page[] Pages =
    [
        new("clients", "Clients", "Details", null),
        new("properties", "Property", "Details", null),
        new("tasks", "Task", "Details", null),
        new(UptickResources.Reports, "Reports", "Details", "reports"),
        new(UptickResources.Invoices, "Billing", "Invoice", "invoices"),
        new(UptickResources.DefectQuotes, "Billing", "Quote", "quotes"),
        new(UptickResources.ServiceQuotes, "Billing", "SalesQuote", "servicequotes")
    ];

    // FyreApp's ids of the records of one endpoint it holds, by Uptick id
    private Task<Dictionary<long, int>> IdsAsync(string resource, List<long> ids, CancellationToken ct) => resource switch
    {
        "clients" => IdsAsync(_db.Clients, ids, ct),
        "properties" => IdsAsync(_db.Properties, ids, ct),
        "tasks" => IdsAsync(_db.ClientTasks, ids, ct),
        UptickResources.Reports => IdsAsync(_db.Reports, ids, ct),
        UptickResources.Invoices => IdsAsync(_db.Invoices, ids, ct),
        UptickResources.DefectQuotes => IdsAsync(_db.DefectQuotes, ids, ct),
        UptickResources.ServiceQuotes => IdsAsync(_db.ServiceQuotes, ids, ct),
        _ => Task.FromResult(new Dictionary<long, int>())
    };

    private static async Task<Dictionary<long, int>> IdsAsync<T>(IQueryable<T> rows, List<long> ids, CancellationToken ct)
        where T : class, IUptickMapped =>
        ids.Count == 0 ? new Dictionary<long, int>()
        : (await rows.AsNoTracking().Where(r => r.UptickId != null && ids.Contains(r.UptickId.Value))
            .Select(r => new { UptickId = r.UptickId!.Value, Id = EF.Property<int>(r, "Id") }).ToListAsync(ct))
        .ToDictionary(r => r.UptickId, r => r.Id);

    // The records, with FyreApp's page for those FyreApp holds
    private async Task<List<RecordLinkVm>> RecordsAsync(IEnumerable<(string Type, long UptickId, string? Name)> records, CancellationToken ct)
    {
        var list = records.DistinctBy(r => (r.Type, r.UptickId)).ToList();
        var held = new Dictionary<string, Dictionary<long, int>>();
        foreach (var page in Pages)
            held[page.Resource] = await IdsAsync(page.Resource, list.Where(r => r.Type == page.Resource).Select(r => r.UptickId).ToList(), ct);

        return list.Select(r =>
        {
            var kind = Kinds.TryGetValue(r.Type, out var k) ? k : r.Type;
            var name = r.Name ?? $"{kind} {r.UptickId}";
            return Pages.FirstOrDefault(p => p.Resource == r.Type) is { } page && held[page.Resource].TryGetValue(r.UptickId, out var id)
                ? new RecordLinkVm(kind, name, page.Controller, page.Action, id)
                : new RecordLinkVm(kind, name, null, null, null);
        }).ToList();
    }

    [GeneratedRegex(@"^(reports|invoices|quotes|servicequotes)/(\d+)/")]
    private static partial Regex PdfPath();

    // An attachment that is a record's PDF: the record's endpoint and Uptick id
    private static (Page Page, long Id)? PdfOf(string? path) =>
        path is { } p && PdfPath().Match(p) is { Success: true } m && long.TryParse(m.Groups[2].Value, out var id)
            && Pages.FirstOrDefault(x => x.Folder == m.Groups[1].Value) is { } page
            ? (page, id) : null;

    // Each attachment as FyreApp holds it: a report's, invoice's or quote's PDF (its page, and FyreApp's copy), or the
    // document file at its path
    private async Task<List<AttachmentVm>> AttachmentsAsync(IEnumerable<EmailAttachment> attachments, CancellationToken ct)
    {
        var list = attachments.ToList();
        var records = list.Select(a => PdfOf(a.Path)).OfType<(Page Page, long Id)>().Distinct().ToList();
        var held = new Dictionary<string, Dictionary<long, int>>();
        var pdfs = new Dictionary<string, Dictionary<long, int>>();
        foreach (var group in records.GroupBy(r => r.Page.Resource))
        {
            var ids = group.Select(r => r.Id).ToList();
            held[group.Key] = await IdsAsync(group.Key, ids, ct);
            pdfs[group.Key] = await UptickFileLinks.PdfsAsync(_db, group.Key, ids.Select(i => (long?)i), ct);
        }
        var paths = list.Where(a => PdfOf(a.Path) == null).Select(a => a.Path).OfType<string>().Distinct().ToList();
        var files = paths.Count == 0 ? new Dictionary<string, int>()
            : (await _db.UptickFiles.AsNoTracking().Where(f => f.Path != null && paths.Contains(f.Path) && f.StorageKey != null)
                .Select(f => new { f.Path, f.Id }).ToListAsync(ct)).GroupBy(f => f.Path!).ToDictionary(g => g.Key, g => g.First().Id);

        return list.Select(a =>
        {
            if (PdfOf(a.Path) is var (page, id))
                return new AttachmentVm(a.Name,
                    held[page.Resource].TryGetValue(id, out var record) ? new RecordLinkVm(page.Resource, a.Name, page.Controller, page.Action, record) : null,
                    pdfs[page.Resource].TryGetValue(id, out var pdf) ? pdf : null);
            return new AttachmentVm(a.Name, null, a.Path != null && files.TryGetValue(a.Path, out var file) ? file : null);
        }).ToList();
    }
}
