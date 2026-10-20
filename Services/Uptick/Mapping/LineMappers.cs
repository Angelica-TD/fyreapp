using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// The lines of Uptick's quotes and invoices, and remarks' histories (docs/uptick-migration.md, 8d). Each is on its
// quote, invoice or remark (one whose parent FyreApp doesn't hold fails); one deleted in Uptick is removed.
internal static class LineLookups
{
    // FyreApp's id by Uptick id, for any mapped table
    public static async Task<Dictionary<long, int>> IdsAsync<T>(IQueryable<T> rows, CancellationToken ct) where T : class, IUptickMapped =>
        (await rows.AsNoTracking().Where(r => r.UptickId != null)
            .Select(r => new { UptickId = r.UptickId!.Value, Id = EF.Property<int>(r, "Id") }).ToListAsync(ct))
        .ToDictionary(r => r.UptickId, r => r.Id);

    public static int? Find(long? uptickId, Dictionary<long, int> rows) =>
        uptickId is { } id && rows.TryGetValue(id, out var found) ? found : null;

    // The line's parent, which it can't be without
    public static int Parent(UptickRaw raw, string relationship, string noun, Dictionary<long, int> rows) =>
        raw.Rel(relationship) is not { } id ? throw new UptickMapException($"It has no {noun}.")
        : rows.TryGetValue(id, out var found) ? found
        : throw new UptickMapException($"Its {noun} ({id}) isn't in FyreApp.");
}

// Uptick's defectquotelineitems -> DefectQuoteLine
public class DefectQuoteLineMapper : UptickEntityMapper<DefectQuoteLine>
{
    private Dictionary<long, int> _quotes = new(), _products = new(), _assets = new(), _remarks = new();

    public DefectQuoteLineMapper(AppDbContext db) : base(db) { }

    public override string Resource => "defectquotelineitems";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _quotes = await LineLookups.IdsAsync(Db.DefectQuotes, ct);
        _products = await LineLookups.IdsAsync(Db.Products, ct);
        _assets = await LineLookups.IdsAsync(Db.Assets, ct);
        _remarks = await LineLookups.IdsAsync(Db.Remarks, ct);
    }

    protected override async Task<Dictionary<long, DefectQuoteLine>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.DefectQuoteLines.Where(l => l.UptickId != null && ids.Contains(l.UptickId.Value)).ToListAsync(ct), l => l.UptickId);
    }

    protected override DefectQuoteLine Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(DefectQuoteLine l, UptickRaw raw)
    {
        l.DefectQuoteId = LineLookups.Parent(raw, "quote", "quote", _quotes);
        l.UptickId = raw.Id;
        l.Index = raw.Int("index") ?? 0;
        l.Description = raw.Text("description");
        l.ProductId = LineLookups.Find(raw.Rel("product"), _products);
        l.AssetId = LineLookups.Find(raw.Rel("asset"), _assets);
        l.RemarkId = LineLookups.Find(raw.Rel("remark"), _remarks);
        l.Quantity = raw.Decimal("quantity") ?? 0;
        l.UnitPrice = raw.Decimal("unit_price");
        l.CostPrice = raw.Decimal("cost_price");
        l.Subtotal = raw.Decimal("subtotal");
        l.Tax = raw.Decimal("gst");
        l.Total = raw.Decimal("total");
    }

    protected override void Deactivate(DefectQuoteLine l) => Db.DefectQuoteLines.Remove(l);
}

// Uptick's invoicelineitems -> InvoiceLine
public class InvoiceLineMapper : UptickEntityMapper<InvoiceLine>
{
    private Dictionary<long, int> _invoices = new(), _products = new();

    public InvoiceLineMapper(AppDbContext db) : base(db) { }

    public override string Resource => "invoicelineitems";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _invoices = await LineLookups.IdsAsync(Db.Invoices, ct);
        _products = await LineLookups.IdsAsync(Db.Products, ct);
    }

    protected override async Task<Dictionary<long, InvoiceLine>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.InvoiceLines.Where(l => l.UptickId != null && ids.Contains(l.UptickId.Value)).ToListAsync(ct), l => l.UptickId);
    }

    protected override InvoiceLine Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(InvoiceLine l, UptickRaw raw)
    {
        l.InvoiceId = LineLookups.Parent(raw, "invoice", "invoice", _invoices);
        l.UptickId = raw.Id;
        l.Index = raw.Int("index") ?? 0;
        l.Description = raw.Text("description");
        l.ProductId = LineLookups.Find(raw.Rel("product"), _products);
        l.Quantity = raw.Decimal("quantity") ?? 0;
        l.UnitPrice = raw.Decimal("unit_price");
        l.Subtotal = raw.Decimal("subtotal");
        l.Tax = raw.Decimal("gst");
        l.Total = raw.Decimal("total");
        l.AccountCode = raw.Text("account_code", 50);
    }

    protected override void Deactivate(InvoiceLine l) => Db.InvoiceLines.Remove(l);
}

// One of Uptick's three sales quote line endpoints -> ServiceQuoteLine of that kind
public abstract class ServiceQuoteLineMapper : UptickEntityMapper<ServiceQuoteLine>
{
    private Dictionary<long, int> _quotes = new();
    protected Dictionary<long, int> Products { get; private set; } = new();
    protected Dictionary<long, int> ServiceTypes { get; private set; } = new();

    protected ServiceQuoteLineMapper(AppDbContext db) : base(db) { }

    protected abstract string Kind { get; }

    public override async Task BeginAsync(CancellationToken ct)
    {
        _quotes = await LineLookups.IdsAsync(Db.ServiceQuotes, ct);
        Products = await LineLookups.IdsAsync(Db.Products, ct);
        ServiceTypes = await LineLookups.IdsAsync(Db.RoutineServiceTypes, ct);
    }

    protected override async Task<Dictionary<long, ServiceQuoteLine>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.ServiceQuoteLines.Where(l => l.Kind == Kind && ids.Contains(l.UptickId)).ToListAsync(ct), l => l.UptickId);
    }

    protected override ServiceQuoteLine Create(UptickRaw raw) => new() { Kind = Kind, UptickId = raw.Id };

    protected override void Map(ServiceQuoteLine l, UptickRaw raw)
    {
        l.ServiceQuoteId = LineLookups.Parent(raw, "servicequote", "sales quote", _quotes);
        l.Kind = Kind;
        l.UptickId = raw.Id;
        l.Index = raw.Int("index") ?? 0;
        l.Description = raw.Text("description");
        l.Quantity = raw.Decimal("quantity") ?? 0;
        l.UnitPrice = raw.Decimal("unit_price");
        MapPrices(l, raw);
    }

    protected abstract void MapPrices(ServiceQuoteLine l, UptickRaw raw);

    protected override void Deactivate(ServiceQuoteLine l) => Db.ServiceQuoteLines.Remove(l);
}

// A product on a sales quote
public class ServiceQuoteProductLineMapper : ServiceQuoteLineMapper
{
    public ServiceQuoteProductLineMapper(AppDbContext db) : base(db) { }
    public override string Resource => "servicequoteproductlineitems";
    protected override string Kind => ServiceQuoteLine.ProductKind;

    protected override void MapPrices(ServiceQuoteLine l, UptickRaw raw)
    {
        l.ProductId = LineLookups.Find(raw.Rel("product"), Products);
        l.CostPrice = raw.Decimal("cost_price");
        l.Subtotal = raw.Decimal("subtotal");
        l.Tax = raw.Decimal("tax");
        l.Total = raw.Decimal("total");
    }
}

// A routine service on a sales quote, priced by the year: fixed, or charged per service ("do and charge")
public class ServiceQuoteRoutineLineMapper : ServiceQuoteLineMapper
{
    private readonly bool _doAndCharge;

    protected ServiceQuoteRoutineLineMapper(AppDbContext db, bool doAndCharge) : base(db) => _doAndCharge = doAndCharge;

    public override string Resource => _doAndCharge ? "servicequotedoandchargelineitems" : "servicequotefixedlineitems";
    protected override string Kind => _doAndCharge ? ServiceQuoteLine.DoAndChargeKind : ServiceQuoteLine.FixedKind;

    protected override void MapPrices(ServiceQuoteLine l, UptickRaw raw)
    {
        l.RoutineServiceTypeId = LineLookups.Find(raw.Rel("routineservicetype"), ServiceTypes);
        l.Subtotal = raw.Decimal("annual_subtotal");
        l.Tax = raw.Decimal("annual_tax");
        l.Total = l.Subtotal is { } subtotal ? subtotal + (l.Tax ?? 0) : null;
        l.EstimatedDuration = raw.Text("estimated_duration", 20);
    }
}

public class ServiceQuoteFixedLineMapper(AppDbContext db) : ServiceQuoteRoutineLineMapper(db, doAndCharge: false);

public class ServiceQuoteDoAndChargeLineMapper(AppDbContext db) : ServiceQuoteRoutineLineMapper(db, doAndCharge: true);

// Uptick's remarkevents -> RemarkEvent: a remark added, verified or resolved, by whom, on which task
public class RemarkEventMapper : UptickEntityMapper<RemarkEvent>
{
    private Dictionary<long, int> _remarks = new(), _users = new(), _tasks = new();

    public RemarkEventMapper(AppDbContext db) : base(db) { }

    public override string Resource => "remarkevents";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _remarks = await LineLookups.IdsAsync(Db.Remarks, ct);
        _users = await LineLookups.IdsAsync(Db.UptickUsers, ct);
        _tasks = await LineLookups.IdsAsync(Db.ClientTasks, ct);
    }

    protected override async Task<Dictionary<long, RemarkEvent>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.RemarkEvents.Where(e => e.UptickId != null && ids.Contains(e.UptickId.Value)).ToListAsync(ct), e => e.UptickId);
    }

    protected override RemarkEvent Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(RemarkEvent e, UptickRaw raw)
    {
        e.RemarkId = LineLookups.Parent(raw, "remark", "remark", _remarks);
        e.UptickId = raw.Id;
        e.Event = raw.Text("event", 5);
        e.Label = raw.Text("event_display", 50);
        e.Notes = raw.Text("notes");
        e.WhenUtc = raw.Time("created");
        e.UptickUserId = LineLookups.Find(raw.Rel("account"), _users);
        e.ClientTaskId = LineLookups.Find(raw.Rel("task"), _tasks);
    }

    protected override void Deactivate(RemarkEvent e) => Db.RemarkEvents.Remove(e);
}
