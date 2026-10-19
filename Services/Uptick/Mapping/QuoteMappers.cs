using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's defectquotes -> DefectQuote, at its property and linked to the task it was raised on. Its totals are
// its line items' (defectquotelineitems). A quote the quotes export placed (ExternalId = its id) is matched and
// updated.
public class DefectQuoteMapper : UptickEntityMapper<DefectQuote>
{
    private Dictionary<long, (int Id, string? Ref, string Name, string? Client)> _properties = new();
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, string> _groups = new();
    private Dictionary<long, (decimal Subtotal, decimal Tax, decimal Total)> _totals = new();

    public DefectQuoteMapper(AppDbContext db) : base(db) { }

    public override string Resource => "defectquotes";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = await QuoteProperties.LoadAsync(Db, ct);
        _tasks = (await Db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
        _groups = await RoutineServiceTypeMapper.RawNamesAsync(Db, "servicegroups", ct);

        _totals = new Dictionary<long, (decimal, decimal, decimal)>();
        foreach (var line in await RemarkMapper.RawAsync(Db, "defectquotelineitems", ct))
        {
            if (RemarkMapper.Rel(line.R, "quote") is not { } quote) continue;
            var (subtotal, tax, total) = _totals.GetValueOrDefault(quote);
            _totals[quote] = (subtotal + Money(line.A, "subtotal"), tax + Money(line.A, "gst"), total + Money(line.A, "total"));
        }
    }

    protected override async Task<Dictionary<long, DefectQuote>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.DefectQuotes
            .Where(q => (q.UptickId != null && ids.Contains(q.UptickId.Value)) || (q.ExternalId != null && texts.Contains(q.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), q => q.UptickId, q => q.ExternalId);
    }

    protected override DefectQuote Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(DefectQuote q, UptickRaw raw)
    {
        if (raw.Rel("property") is not { } propertyId) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(propertyId, out var property)) throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");
        (decimal Subtotal, decimal Tax, decimal Total)? totals = _totals.TryGetValue(raw.Id, out var t) ? t : null;

        q.UptickId = raw.Id;
        q.ExternalId = raw.Id.ToString();
        q.PropertyId = property.Id;
        q.PropertyRef = RoutineServiceTypeMapper.Cap(property.Ref, 64);
        q.PropertyName = RoutineServiceTypeMapper.Cap(property.Name, 300);
        q.ClientName = RoutineServiceTypeMapper.Cap(property.Client, 300);
        q.ClientTaskId = raw.Rel("task") is { } task && _tasks.TryGetValue(task, out var taskId) ? taskId : null;
        q.Reference = raw.Text("ref", 30) ?? $"Quote {raw.Id}";
        q.Status = raw.Text("status", 30)?.ToUpperInvariant();
        q.Date = raw.Date("date");
        q.DueDate = raw.Date("due_date");
        q.ExpiryDate = raw.Date("expiry_date");
        q.SubmittedUtc = raw.Time("status_changed_submitted");
        q.AuthorisedDate = QuoteProperties.SydneyDate(raw.Time("authorisation_date"));
        q.Description = raw.Text("description");
        q.ScopeOfWorks = raw.Text("scope_of_works");
        q.Severity = raw.Text("severity", 100);
        q.ServiceGroup = RoutineServiceTypeMapper.Cap(raw.Rel("servicegroup") is { } g ? _groups.GetValueOrDefault(g) : null, 200);
        q.Subtotal = totals?.Subtotal;
        q.Tax = totals?.Tax;
        q.Total = totals?.Total;
    }

    protected override void Deactivate(DefectQuote q) => q.Status = "DELETED";

    private static decimal Money(System.Text.Json.JsonElement a, string name) =>
        UptickRaw.Double(a, name) is { } d ? Math.Round((decimal)d, 2) : 0;
}

// Uptick's servicequotes ("Sales quotes" in Uptick) -> ServiceQuote, at its property. A sales quote the sales quotes
// export placed (ExternalId = its id) is matched and updated.
public class ServiceQuoteMapper : UptickEntityMapper<ServiceQuote>
{
    private Dictionary<long, (int Id, string? Ref, string Name, string? Client)> _properties = new();
    private Dictionary<long, string> _templates = new();

    public ServiceQuoteMapper(AppDbContext db) : base(db) { }

    public override string Resource => "servicequotes";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = await QuoteProperties.LoadAsync(Db, ct);
        _templates = await RoutineServiceTypeMapper.RawNamesAsync(Db, "documenttemplates", ct);
    }

    protected override async Task<Dictionary<long, ServiceQuote>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.ServiceQuotes
            .Where(q => (q.UptickId != null && ids.Contains(q.UptickId.Value)) || (q.ExternalId != null && texts.Contains(q.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), q => q.UptickId, q => q.ExternalId);
    }

    protected override ServiceQuote Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(ServiceQuote q, UptickRaw raw)
    {
        (int Id, string? Ref, string Name, string? Client)? property =
            raw.Rel("property_object") is { } p && _properties.TryGetValue(p, out var found) ? found : null;
        if (raw.Rel("property_object") is { } missing && property == null)
            throw new UptickMapException($"Its property ({missing}) isn't in FyreApp.");

        q.UptickId = raw.Id;
        q.ExternalId = raw.Id.ToString();
        q.PropertyId = property?.Id;
        q.PropertyName = RoutineServiceTypeMapper.Cap(property?.Name ?? raw.Text("property_name"), 300);
        q.Address = Address(raw) ?? q.Address;
        q.ClientName = RoutineServiceTypeMapper.Cap(property?.Client ?? raw.Text("client_name"), 300);
        q.Ref = raw.Text("ref", 30) ?? $"Sales quote {raw.Id}";
        q.Status = raw.Text("status", 30)?.ToUpperInvariant();
        q.Description = raw.Text("description");
        q.ScopeOfWorksHtml = raw.Text("scope_of_works");
        q.Template = RoutineServiceTypeMapper.Cap(raw.Text("template_name")
            ?? (raw.Rel("documenttemplate") is { } t ? _templates.GetValueOrDefault(t) : null), 200);
        q.Created = QuoteProperties.SydneyDate(raw.Time("created"));
        q.ExpiryDate = raw.Date("expiry_date");
        q.Subtotal = raw.Decimal("product_subtotal");
    }

    protected override void Deactivate(ServiceQuote q) => q.Status = "DELETED";

    private static string? Address(UptickRaw raw) =>
        UptickRaw.Get(raw.A, "property_address") is { ValueKind: System.Text.Json.JsonValueKind.Object } a
        && UptickRaw.Str(a, "display") is { Length: > 0 } display ? display : null;
}

internal static class QuoteProperties
{
    // Mapped properties by Uptick id, with their ref, name and client
    public static async Task<Dictionary<long, (int Id, string? Ref, string Name, string? Client)>> LoadAsync(AppDbContext db, CancellationToken ct) =>
        (await db.Properties.AsNoTracking().Where(p => p.UptickId != null)
            .Select(p => new { UptickId = p.UptickId!.Value, p.Id, p.ExternalId, p.Name, Client = p.Client.Name }).ToListAsync(ct))
        .ToDictionary(p => p.UptickId, p => (p.Id, p.ExternalId, p.Name, (string?)p.Client));

    // A moment as its calendar day in Sydney (midnight UTC, as the exports' dates are kept)
    public static DateTime? SydneyDate(DateTime? utc) =>
        utc is { } t ? DateTime.SpecifyKind(t.ToSydney().Date, DateTimeKind.Utc) : null;
}
