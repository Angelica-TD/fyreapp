using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's invoices -> Invoice, for its client, at its property (some are the client's alone) and linked to the task
// it bills. Its credit total is its credit notes'.
public class InvoiceMapper : UptickEntityMapper<Invoice>
{
    private Dictionary<long, (int Id, string? Ref, string Name, string? Client)> _properties = new();
    private Dictionary<long, string> _clients = new();
    private Dictionary<long, (int Id, string? Ref)> _tasks = new();
    private Dictionary<long, string> _cards = new();
    private Dictionary<long, decimal> _credits = new();

    public InvoiceMapper(AppDbContext db) : base(db) { }

    public override string Resource => "invoices";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = await QuoteProperties.LoadAsync(Db, ct);
        _clients = (await Db.Clients.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Name);
        _tasks = (await Db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id, t.Ref }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => (t.Id, t.Ref));
        _cards = await RoutineServiceTypeMapper.RawNamesAsync(Db, "billingcards", ct);

        _credits = new Dictionary<long, decimal>();
        foreach (var note in await RemarkMapper.RawAsync(Db, "creditnotes", ct))
        {
            if (RemarkMapper.Rel(note.R, "invoice") is not { } invoice || !CreditNoteMapper.Counts(note.A)) continue;
            _credits[invoice] = _credits.GetValueOrDefault(invoice) + CreditNoteMapper.Total(note.A);
        }
    }

    protected override async Task<Dictionary<long, Invoice>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Invoices
            .Where(i => i.UptickId != null && ids.Contains(i.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, i => i.UptickId);
    }

    protected override Invoice Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Invoice i, UptickRaw raw)
    {
        (int Id, string? Ref, string Name, string? Client)? property = null;
        if (raw.Rel("property") is { } propertyId)
            property = _properties.TryGetValue(propertyId, out var found) ? found
                : throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");
        (int Id, string? Ref)? task = raw.Rel("task") is { } t && _tasks.TryGetValue(t, out var ft) ? ft : null;

        i.UptickId = raw.Id;
        i.Number = raw.Text("number", 50) ?? $"Invoice {raw.Id}";
        i.InvoiceRef = raw.Text("ref", 100);
        i.Description = raw.Text("description");
        i.PropertyId = property?.Id;
        i.PropertyRef = RoutineServiceTypeMapper.Cap(property?.Ref, 64);
        i.PropertyName = RoutineServiceTypeMapper.Cap(property?.Name, 300);
        i.ClientName = RoutineServiceTypeMapper.Cap((raw.Rel("client") is { } c ? _clients.GetValueOrDefault(c) : null) ?? property?.Client, 300);
        i.BillingCardName = RoutineServiceTypeMapper.Cap(raw.Rel("billingcard") is { } b ? _cards.GetValueOrDefault(b) : null, 300);
        i.ClientTaskId = task?.Id;
        i.TaskRef = RoutineServiceTypeMapper.Cap(task?.Ref, 20);
        i.Date = raw.Date("date");
        i.DueDate = raw.Date("due_date");
        i.Subtotal = raw.Decimal("subtotal");
        i.Tax = raw.Decimal("gst");
        i.Total = raw.Decimal("total");
        i.CreditTotal = _credits.TryGetValue(raw.Id, out var credit) ? credit : null;
        i.Status = raw.Text("status", 30)?.ToUpperInvariant();
    }

    protected override void Deactivate(Invoice i) => i.Status = "DELETED";
}

// Uptick's creditnotes -> CreditNote, on its invoice.
public class CreditNoteMapper : UptickEntityMapper<CreditNote>
{
    private Dictionary<long, (int Id, string Number, string? PropertyRef)> _invoices = new();

    public CreditNoteMapper(AppDbContext db) : base(db) { }

    public override string Resource => "creditnotes";

    public override async Task BeginAsync(CancellationToken ct) =>
        _invoices = (await Db.Invoices.AsNoTracking().Where(i => i.UptickId != null)
                .Select(i => new { UptickId = i.UptickId!.Value, i.Id, i.Number, i.PropertyRef }).ToListAsync(ct))
            .ToDictionary(i => i.UptickId, i => (i.Id, i.Number, i.PropertyRef));

    protected override async Task<Dictionary<long, CreditNote>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.CreditNotes
            .Where(c => c.UptickId != null && ids.Contains(c.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, c => c.UptickId);
    }

    protected override CreditNote Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(CreditNote c, UptickRaw raw)
    {
        if (raw.Rel("invoice") is not { } invoiceId) throw new UptickMapException("It has no invoice.");
        if (!_invoices.TryGetValue(invoiceId, out var invoice)) throw new UptickMapException($"Its invoice ({invoiceId}) isn't in FyreApp.");

        c.UptickId = raw.Id;
        c.InvoiceId = invoice.Id;
        c.InvoiceNumber = invoice.Number;
        c.PropertyRef = invoice.PropertyRef;
        c.Number = raw.Text("number", 50) ?? $"Credit note {raw.Id}";
        c.Status = raw.Time("deleted") != null ? "DELETED" : raw.Text("status", 30)?.ToUpperInvariant();
        c.Date = raw.Date("date");
        c.Description = raw.Text("description");
        c.Total = raw.Decimal("total");
    }

    protected override void Deactivate(CreditNote c) => c.Status = "DELETED";

    // A credit note counts against its invoice unless it's deleted or voided
    internal static bool Counts(System.Text.Json.JsonElement a) =>
        UptickRaw.Get(a, "deleted").ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined
        && UptickRaw.Str(a, "status") is not ("VOIDED" or "DELETED");

    internal static decimal Total(System.Text.Json.JsonElement a) =>
        UptickRaw.Str(a, "total") is { } s && decimal.TryParse(s, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d
        : UptickRaw.Double(a, "total") is { } n ? (decimal)n : 0;
}
