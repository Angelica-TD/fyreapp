using System.Text.Json;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's remarks -> Remark, on the asset it was raised against (and so at that asset's property: Uptick's remark
// has no property of its own). The task it was raised on comes from its "Added" remark event; its repair task is
// the task of a service task done for it (a Repair task first, else the latest); its quote is its most progressed
// defect quote.
public class RemarkMapper : UptickEntityMapper<Remark>
{
    private Dictionary<long, (int Id, int PropertyId, string? Location)> _assets = new();
    private Dictionary<long, (int Id, string Label)> _types = new();
    private Dictionary<long, int?> _raisedOn = new();
    private Dictionary<long, (int Id, long? UptickId, string? Ref, string? Status)> _repairs = new();
    private Dictionary<long, (int Id, string Ref, string? Status, DateTime? Date)> _quotes = new();

    public RemarkMapper(AppDbContext db) : base(db) { }

    public override string Resource => "remarks";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _assets = (await Db.Assets.AsNoTracking().Where(a => a.UptickId != null)
                .Select(a => new { UptickId = a.UptickId!.Value, a.Id, a.PropertyId, a.Location }).ToListAsync(ct))
            .ToDictionary(a => a.UptickId, a => (a.Id, a.PropertyId, a.Location));
        _types = (await Db.RemarkTypes.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id, t.Label }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => (t.Id, t.Label));
        var taskIds = (await Db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);

        // The task each remark was added on, from the earliest "A" event
        _raisedOn = new Dictionary<long, int?>();
        foreach (var e in (await RawAsync(Db, "remarkevents", ct)).OrderBy(e => e.Id))
            if (UptickRaw.Str(e.A, "event") == "A" && Rel(e.R, "remark") is { } remark && Rel(e.R, "task") is { } task
                && !_raisedOn.ContainsKey(remark))
                _raisedOn[remark] = taskIds.TryGetValue(task, out var taskId) ? taskId : null;

        // Repair tasks: the tasks of service tasks done for a remark, with Uptick's status for them
        var statuses = new Dictionary<long, string?>();
        foreach (var t in await RawAsync(Db, "tasks", ct))
            statuses[t.Id] = UptickRaw.Get(t.R, "status") is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty("data", out var d)
                && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var sid) ? sid.GetString() : null;
        var lines = await Db.ServiceTasks.AsNoTracking().Where(s => s.UptickRemarkId != null)
            .Select(s => new { Remark = s.UptickRemarkId!.Value, Task = s.ClientTask })
            .ToListAsync(ct);
        _repairs = lines.GroupBy(l => l.Remark).ToDictionary(g => g.Key, g =>
        {
            var task = g.Select(l => l.Task).OrderByDescending(t => t.Category == "Repair").ThenByDescending(t => t.CreatedUtc).First();
            return (task.Id, task.UptickId, task.Ref, task.UptickId is { } id ? statuses.GetValueOrDefault(id) : null);
        });

        _quotes = (await Db.DefectQuotes.AsNoTracking().Where(q => q.UptickId != null)
                .Select(q => new { UptickId = q.UptickId!.Value, q.Id, q.Reference, q.Status, q.Date }).ToListAsync(ct))
            .ToDictionary(q => q.UptickId, q => (q.Id, q.Reference, q.Status, q.Date));
    }

    protected override async Task<Dictionary<long, Remark>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Remarks
            .Where(r => r.UptickId != null && ids.Contains(r.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, r => r.UptickId);
    }

    protected override Remark Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Remark r, UptickRaw raw)
    {
        if (raw.Rel("asset") is not { } assetId) throw new UptickMapException("It has no asset.");
        if (!_assets.TryGetValue(assetId, out var asset)) throw new UptickMapException($"Its asset ({assetId}) isn't in FyreApp.");
        (int Id, string Label)? type = raw.Rel("type") is { } t && _types.TryGetValue(t, out var found) ? found : null;
        var severity = raw.Int("severity");
        (int Id, long? UptickId, string? Ref, string? Status)? repair = _repairs.TryGetValue(raw.Id, out var rep) ? rep : null;
        (int Id, string Ref, string? Status, DateTime? Date)? quote =
            raw.Rel("most_progressed_defectquote") is { } q && _quotes.TryGetValue(q, out var qf) ? qf : null;

        r.UptickId = raw.Id;
        r.PropertyId = asset.PropertyId;
        r.AssetId = asset.Id;
        r.RemarkTypeId = type?.Id;
        r.RemarkType = type?.Label ?? raw.Text("label");
        r.Status = StatusLabel(raw.Text("status"));
        r.Severity = severity;
        r.SeverityLabel = RemarkSeverity.Label(severity) ?? raw.Text("get_severity_display");
        r.Description = raw.Text("description");
        r.Resolution = raw.Text("resolution");
        r.Notes = raw.Text("notes");
        r.Location = raw.Text("location") ?? asset.Location;
        r.Active = raw.Bool("is_active") ?? true;
        r.RaisedUtc = raw.Time("identified") ?? raw.Time("created");
        r.InactiveUtc = raw.Time("inactive_date");
        r.RaisedOnTaskId = _raisedOn.GetValueOrDefault(raw.Id);
        r.DefectQuoteId = quote?.Id;
        r.QuoteRef = quote?.Ref;
        r.QuoteStatus = quote?.Status;
        r.QuotedDate = quote?.Date;
        r.RepairTaskId = repair?.Id;
        r.RepairTaskRef = repair?.Ref;
        r.RepairTaskStatus = repair?.Status;
    }

    protected override void Deactivate(Remark r) => r.Active = false;

    // As the remarks export words a status: NEEDS_QUOTING -> "Needs Quoting"
    internal static string? StatusLabel(string? status) => status switch
    {
        null => null,
        "EXPIRED_DECLINED" => "Expired/Declined",
        _ => string.Join(' ', status.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w[..1] + w[1..].ToLowerInvariant()))
    };

    internal static long? Rel(JsonElement relationships, string name) =>
        UptickRaw.Get(relationships, name) is { ValueKind: JsonValueKind.Object } rel && rel.TryGetProperty("data", out var d)
        && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var id) && long.TryParse(id.ToString(), out var n) ? n : null;

    // An endpoint's raw records with numeric ids, read for lookups
    internal static async Task<List<(long Id, JsonElement A, JsonElement R)>> RawAsync(AppDbContext db, string resource, CancellationToken ct)
    {
        var rows = await db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Attributes, r.Relationships })
            .ToListAsync(ct);
        var list = new List<(long, JsonElement, JsonElement)>(rows.Count);
        foreach (var r in rows)
        {
            if (!long.TryParse(r.UptickId, out var id)) continue;
            using var a = JsonDocument.Parse(r.Attributes);
            using var rel = JsonDocument.Parse(r.Relationships);
            list.Add((id, a.RootElement.Clone(), rel.RootElement.Clone()));
        }
        return list;
    }
}

// Uptick's reports -> Report, at its property and linked to the task it was done on.
public class ReportMapper : UptickEntityMapper<Report>
{
    private Dictionary<long, int> _properties = new();
    private Dictionary<long, (int Id, string? Ref, string Title)> _tasks = new();
    private Dictionary<long, string> _users = new();
    private Dictionary<long, string> _types = new();

    public ReportMapper(AppDbContext db) : base(db) { }

    public override string Resource => "reports";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Id);
        _tasks = (await Db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id, t.Ref, t.Title }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => (t.Id, t.Ref, t.Title));
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Name }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => u.Name);
        _types = await RoutineServiceTypeMapper.RawNamesAsync(Db, "reporttypes", ct);
    }

    protected override async Task<Dictionary<long, Report>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Reports
            .Where(r => r.UptickId != null && ids.Contains(r.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, r => r.UptickId);
    }

    protected override Report Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Report r, UptickRaw raw)
    {
        if (raw.Rel("property") is not { } propertyId) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(propertyId, out var property)) throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");
        (int Id, string? Ref, string Title)? task = raw.Rel("task") is { } t && _tasks.TryGetValue(t, out var found) ? found : null;

        r.UptickId = raw.Id;
        r.PropertyId = property;
        r.Ref = raw.Text("ref");
        r.ReportType = raw.Text("title") ?? (raw.Rel("type") is { } type ? _types.GetValueOrDefault(type) : null) ?? raw.Text("get_title");
        r.IssuedDate = raw.Date("issued");
        r.InspectedDate = raw.Date("inspected");
        r.Compliant = raw.Bool("compliant");
        r.ClientTaskId = task?.Id;
        r.TaskRef = task?.Ref;
        r.TaskName = task?.Title;
        r.Technician = raw.Rel("technician") is { } tech ? _users.GetValueOrDefault(tech) : null;
        r.Author = raw.Rel("author") is { } author ? _users.GetValueOrDefault(author) : null;
        r.Published = raw.Bool("is_published") ?? false;
        r.Amendment = raw.Bool("is_amendment") ?? false;
        r.GeneralNote = raw.Text("general_note");
        r.CriticalNote = raw.Text("critical_note");
    }

    // Gone from Uptick: nothing in FyreApp points at a report, and the raw copy keeps it
    protected override void Deactivate(Report r) => Db.Reports.Remove(r);
}

// Uptick's reportitems -> ReportItem: each asset's line on a report, on the report and the asset (when FyreApp holds
// it). One deleted in Uptick is removed.
public class ReportItemMapper : UptickEntityMapper<ReportItem>
{
    private Dictionary<long, int> _reports = new();
    private Dictionary<long, int> _assets = new();

    public ReportItemMapper(AppDbContext db) : base(db) { }

    public override string Resource => "reportitems";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _reports = (await Db.Reports.AsNoTracking().Where(r => r.UptickId != null)
                .Select(r => new { UptickId = r.UptickId!.Value, r.Id }).ToListAsync(ct))
            .ToDictionary(r => r.UptickId, r => r.Id);
        _assets = (await Db.Assets.AsNoTracking().Where(a => a.UptickId != null)
                .Select(a => new { UptickId = a.UptickId!.Value, a.Id }).ToListAsync(ct))
            .ToDictionary(a => a.UptickId, a => a.Id);
    }

    protected override async Task<Dictionary<long, ReportItem>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.ReportItems.Where(i => i.UptickId != null && ids.Contains(i.UptickId.Value)).ToListAsync(ct), i => i.UptickId);
    }

    protected override ReportItem Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(ReportItem i, UptickRaw raw)
    {
        if (raw.Rel("report") is not { } reportId) throw new UptickMapException("It has no report.");
        if (!_reports.TryGetValue(reportId, out var report)) throw new UptickMapException($"Its report ({reportId}) isn't in FyreApp.");

        i.UptickId = raw.Id;
        i.ReportId = report;
        i.AssetId = raw.Rel("asset") is { } a && _assets.TryGetValue(a, out var asset) ? asset : null;
        i.Label = raw.Text("label", 500);
        i.Ref = raw.Text("ref", 100);
        i.Location = raw.Text("location", 500);
        i.Barcode = raw.Text("barcode", 100);
        i.Make = raw.Text("make", 200);
        i.Model = raw.Text("model", 200);
        i.Size = raw.Text("size", 200);
        i.Standard = raw.Text("standard", 200);
        i.Compliant = raw.Bool("compliant");
        i.InspectedDate = raw.Date("inspected_date");
        i.ServicedDate = raw.Date("serviced_date");
        i.BaseDate = raw.Date("base_date");
    }

    protected override void Deactivate(ReportItem i) => Db.ReportItems.Remove(i);
}
