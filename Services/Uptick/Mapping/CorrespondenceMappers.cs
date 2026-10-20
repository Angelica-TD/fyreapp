using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's emails: correspondence (each email), correspondenceobjectlinks (the records each is about) and dispatches
// (a task's invoice and reports sent to the customer). One deleted in Uptick is removed; nothing points at it.
internal static class EmailText
{
    // An attachment's path. A link out of Uptick ("https://…/x.pdf?X-Amz-…") is kept without its query: the signature
    // in it has long run out, and isn't one to keep.
    public static string? Path(string? path) =>
        path is null ? null
        : path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && path.IndexOf('?') is var q and >= 0 ? path[..q]
        : path;

    public static List<string> Strings(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!.Trim())
                .Where(s => s.Length > 0).ToList()
            : [];

    public static string? Join(IEnumerable<string> values) => string.Join(", ", values) is { Length: > 0 } s ? s : null;
}

// Uptick's correspondence -> Correspondence
public class CorrespondenceMapper : UptickEntityMapper<Correspondence>
{
    private Dictionary<long, int> _users = new();

    public CorrespondenceMapper(AppDbContext db) : base(db) { }

    public override string Resource => "correspondence";

    public override async Task BeginAsync(CancellationToken ct) => _users = await TaskWorkLookups.UsersAsync(Db, ct);

    protected override async Task<Dictionary<long, Correspondence>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.Correspondence.Where(c => c.UptickId != null && ids.Contains(c.UptickId.Value)).ToListAsync(ct), c => c.UptickId);
    }

    protected override Correspondence Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Correspondence c, UptickRaw raw)
    {
        c.UptickId = raw.Id;
        c.Subject = raw.Text("subject", 1000);
        c.From = raw.Text("from_str", 500);
        c.FromName = raw.Text("from_name", 300);
        c.To = raw.Text("to_str");
        c.Cc = raw.Text("cc_str");
        c.Bcc = EmailText.Join(EmailText.Strings(UptickRaw.Get(raw.A, "bcc")));
        c.ReplyTo = raw.Text("reply_to_str", 500);
        c.SentUtc = raw.Time("created");
        c.SentById = raw.Rel("sent_by") is { } u && _users.TryGetValue(u, out var user) ? user : null;
        c.IsStarred = raw.Bool("is_starred") ?? false;
        c.BodyHtml = UptickRaw.Str(raw.A, "body_html");
        c.Attachments = UptickRaw.Get(raw.A, "attachments") is { ValueKind: JsonValueKind.Object } files
            ? files.EnumerateObject()
                .Select(f => new EmailAttachment(f.Name, EmailText.Path(f.Value.ValueKind == JsonValueKind.String ? f.Value.GetString() : null)))
                .ToList()
            : [];
    }

    protected override void Deactivate(Correspondence c) => Db.Correspondence.Remove(c);
}

// Uptick's correspondenceobjectlinks -> CorrespondenceLink: a record an email is about, by its kind and Uptick id
public class CorrespondenceLinkMapper : UptickEntityMapper<CorrespondenceLink>
{
    // Uptick's model names (contenttypes) as the endpoint names FyreApp files records under
    private static readonly Dictionary<string, string> Resources = new()
    {
        ["client"] = "clients", ["property"] = "properties", ["task"] = "tasks", ["invoice"] = "invoices",
        ["defectquote"] = "defectquotes", ["servicequote"] = "servicequotes", ["contractor"] = "contractors",
        ["user"] = "users", ["issuance"] = "dispatches", ["formresponse"] = "dynamicformresponses"
    };

    private Dictionary<long, int> _emails = new();
    private Dictionary<long, JsonElement> _contentTypes = new();

    public CorrespondenceLinkMapper(AppDbContext db) : base(db) { }

    public override string Resource => "correspondenceobjectlinks";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _emails = (await Db.Correspondence.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Id);
        _contentTypes = await TaskWorkLookups.RawAsync(Db, "contenttypes", ct);
    }

    protected override async Task<Dictionary<long, CorrespondenceLink>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.CorrespondenceLinks.Where(l => l.UptickId != null && ids.Contains(l.UptickId.Value)).ToListAsync(ct), l => l.UptickId);
    }

    protected override CorrespondenceLink Create(UptickRaw raw) => new() { UptickId = raw.Id };

    public static string? RecordType(string? model) =>
        model is null ? null : Resources.TryGetValue(model, out var resource) ? resource : model;

    protected override void Map(CorrespondenceLink l, UptickRaw raw)
    {
        var email = raw.Rel("correspondence") is not { } e ? throw new UptickMapException("It has no email.")
            : _emails.TryGetValue(e, out var id) ? id
            : throw new UptickMapException($"Its email ({e}) isn't in FyreApp.");
        var type = raw.Rel("object_contenttype") is { } c && _contentTypes.TryGetValue(c, out var ct)
            ? RecordType(UptickRaw.Str(ct, "model"))
            : null;

        l.UptickId = raw.Id;
        l.CorrespondenceId = email;
        l.RecordType = RoutineServiceTypeMapper.Cap(type, 50) ?? throw new UptickMapException("Its record's kind isn't known.");
        l.RecordUptickId = UptickRaw.Long(raw.A, "object_id") ?? throw new UptickMapException("It has no record.");
        l.Name = raw.Text("name", 300);
    }

    protected override void Deactivate(CorrespondenceLink l) => Db.CorrespondenceLinks.Remove(l);
}

// Uptick's dispatches -> Dispatch: a task's invoice and reports sent to the customer, and each email that went
public class DispatchMapper : UptickEntityMapper<Dispatch>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, int> _users = new();
    private Dictionary<long, int> _properties = new();
    private Dictionary<long, int> _invoices = new();

    public DispatchMapper(AppDbContext db) : base(db) { }

    public override string Resource => "dispatches";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = await TaskWorkLookups.TasksAsync(Db, ct);
        _users = await TaskWorkLookups.UsersAsync(Db, ct);
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Id);
        _invoices = (await Db.Invoices.AsNoTracking().Where(i => i.UptickId != null)
                .Select(i => new { UptickId = i.UptickId!.Value, i.Id }).ToListAsync(ct))
            .ToDictionary(i => i.UptickId, i => i.Id);
    }

    protected override async Task<Dictionary<long, Dispatch>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.Dispatches.Where(d => d.UptickId != null && ids.Contains(d.UptickId.Value)).ToListAsync(ct), d => d.UptickId);
    }

    protected override Dispatch Create(UptickRaw raw) => new() { UptickId = raw.Id };

    private static int? Find(long? uptickId, Dictionary<long, int> rows) =>
        uptickId is { } id && rows.TryGetValue(id, out var found) ? found : null;

    protected override void Map(Dispatch d, UptickRaw raw)
    {
        d.UptickId = raw.Id;
        d.Ref = raw.Text("ref", 50);
        d.Status = raw.Text("status", 30);
        d.Title = raw.Text("__str__", 500);
        d.ClientTaskId = Find(raw.Rel("task"), _tasks);
        d.PropertyId = Find(raw.Rel("property"), _properties);
        d.InvoiceId = Find(raw.Rel("invoice"), _invoices);
        d.SenderId = Find(raw.Rel("sender"), _users);
        d.IssueInvoice = raw.Bool("issue_invoice") ?? false;
        d.CreatedUtc = raw.Time("created");
        d.Emails = UptickRaw.Get(raw.A, "email_data") is { ValueKind: JsonValueKind.Array } emails
            ? emails.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(Email).ToList()
            : [];
    }

    private static DispatchEmail Email(JsonElement e) => new(
        EmailText.Strings(UptickRaw.Get(e, "to")), EmailText.Strings(UptickRaw.Get(e, "cc")), EmailText.Strings(UptickRaw.Get(e, "bcc")),
        UptickRaw.Str(e, "from"), UptickRaw.Str(e, "reply_to"), UptickRaw.Str(e, "subject"), UptickRaw.Str(e, "attention"),
        UptickRaw.Str(e, "body_html"),
        UptickRaw.Get(e, "attachments") is { ValueKind: JsonValueKind.Array } files
            ? files.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object)
                .Select(f => new EmailAttachment(UptickRaw.Str(f, "name") ?? "Attachment", AttachmentPath(f)))
                .ToList()
            : []);

    // A report not issued yet when the dispatch was made has the path "reports/None/…"; its object ("Report:1") says
    // which report it is
    private static string? AttachmentPath(JsonElement f)
    {
        var path = EmailText.Path(UptickRaw.Str(f, "path"));
        if (path == null || !path.StartsWith("reports/None/", StringComparison.Ordinal)) return path;
        var report = EmailText.Strings(UptickRaw.Get(f, "objects")).FirstOrDefault(o => o.StartsWith("Report:", StringComparison.Ordinal));
        return report != null && long.TryParse(report["Report:".Length..], out var id) ? $"reports/{id}/{path["reports/None/".Length..]}" : path;
    }

    protected override void Deactivate(Dispatch d) => Db.Dispatches.Remove(d);
}
