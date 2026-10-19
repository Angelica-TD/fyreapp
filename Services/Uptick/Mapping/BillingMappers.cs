using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's billingcards -> BillingCard: who Uptick bills. Mapped first, so clients and properties can link to
// theirs. A card the billing cards export placed (ExternalId = its id) is matched and updated.
public class BillingCardMapper : UptickEntityMapper<BillingCard>
{
    private Dictionary<long, string> _organisations = new();
    private Dictionary<long, int> _invoices = new();

    public BillingCardMapper(AppDbContext db) : base(db) { }

    public override string Resource => "billingcards";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _organisations = await RoutineServiceTypeMapper.RawNamesAsync(Db, "accountingorganisations", ct);
        _invoices = (await RemarkMapper.RawAsync(Db, "invoices", ct))
            .Select(i => RemarkMapper.Rel(i.R, "billingcard"))
            .Where(c => c != null)
            .GroupBy(c => c!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    // Mapped cards by Uptick id
    internal static async Task<Dictionary<long, int>> IdsAsync(AppDbContext db, CancellationToken ct) =>
        (await db.BillingCards.AsNoTracking().Where(c => c.UptickId != null)
            .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct))
        .ToDictionary(c => c.UptickId, c => c.Id);

    protected override async Task<Dictionary<long, BillingCard>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.BillingCards
            .Where(c => (c.UptickId != null && ids.Contains(c.UptickId.Value)) || (c.ExternalId != null && texts.Contains(c.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), c => c.UptickId, c => c.ExternalId);
    }

    protected override BillingCard Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(BillingCard c, UptickRaw raw)
    {
        c.UptickId = raw.Id;
        c.ExternalId = raw.Id.ToString();
        c.Name = raw.Text("name", 300) ?? $"Billing card {raw.Id}";
        c.Active = raw.Bool("is_active") ?? true;
        c.Ref = raw.Text("ref", 100);
        c.BusinessNumber = raw.Text("abn", 50);
        c.PostalAttention = raw.Text("postal_attention", 200);
        c.PostalEmail = raw.Text("postal_email", 300);
        c.PostalMobile = raw.Text("postal_mobile", 50);
        c.PostalBusinessHours = raw.Text("postal_bh", 50);
        c.PostalAddress = Address(raw, "postal_address");
        c.SiteAddress = Address(raw, "site_address");
        c.NumberOfInvoices = _invoices.GetValueOrDefault(raw.Id);
        c.AccountingOrganisation = RoutineServiceTypeMapper.Cap(
            raw.Rel("accounting_organisation") is { } o ? _organisations.GetValueOrDefault(o) : null, 200);
        c.DefaultTaxCode = raw.Text("default_taxcode", 50);
        c.CreditLimit = raw.Decimal("credit_limit");
    }

    protected override void Deactivate(BillingCard c) => c.Active = false;

    // Uptick's address object as one line: its display, else its parts
    internal static string? Address(UptickRaw raw, string name)
    {
        if (UptickRaw.Get(raw.A, name) is not { ValueKind: JsonValueKind.Object } a) return null;
        if (UptickRaw.Str(a, "display") is { Length: > 0 } display) return display.Trim();
        var parts = new[] { UptickRaw.Str(a, "streetline"), UptickRaw.Str(a, "city"), UptickRaw.Str(a, "state"), UptickRaw.Str(a, "postal_code") }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }
}

// Uptick's billingcontracts -> BillingContract, at its property, with its billing card by name. A contract the
// billing contracts export placed (ExternalId = its id) is matched and updated.
public class BillingContractMapper : UptickEntityMapper<BillingContract>
{
    private Dictionary<long, (int Id, string? Ref, string Name, string? Client)> _properties = new();
    private Dictionary<long, string> _cards = new();

    public BillingContractMapper(AppDbContext db) : base(db) { }

    public override string Resource => "billingcontracts";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = await QuoteProperties.LoadAsync(Db, ct);
        _cards = (await Db.BillingCards.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Name);
    }

    protected override async Task<Dictionary<long, BillingContract>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.BillingContracts
            .Where(c => (c.UptickId != null && ids.Contains(c.UptickId.Value)) || (c.ExternalId != null && texts.Contains(c.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), c => c.UptickId, c => c.ExternalId);
    }

    protected override BillingContract Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(BillingContract c, UptickRaw raw)
    {
        if (raw.Rel("property") is not { } propertyId) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(propertyId, out var property)) throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");

        c.UptickId = raw.Id;
        c.ExternalId = raw.Id.ToString();
        c.PropertyId = property.Id;
        c.PropertyRef = RoutineServiceTypeMapper.Cap(property.Ref, 64);
        c.PropertyName = RoutineServiceTypeMapper.Cap(property.Name, 300);
        c.ClientName = RoutineServiceTypeMapper.Cap(property.Client, 300);
        c.BillingCardName = RoutineServiceTypeMapper.Cap(raw.Rel("billingcard") is { } b ? _cards.GetValueOrDefault(b) : null, 300);
        c.Name = raw.Text("name", 300) ?? $"Billing contract {raw.Id}";
        c.Active = raw.Bool("is_active") ?? false;
        c.Type = Label(raw.Text("type"), TypeLabels);
        c.Frequency = Label(raw.Text("recurrence"), FrequencyLabels);
        c.InvoiceMode = Label(raw.Text("invoice_mode"), InvoiceModeLabels);
        c.StartDate = raw.Date("start_date");
        c.FinishDate = raw.Date("finish_date");
        c.NextDue = raw.Date("next_due");
        c.PeriodBegin = raw.Date("period_begin");
        c.PeriodEnd = raw.Date("period_end");
        c.ReviewDate = raw.Date("review_date");
        c.AnnualValue = raw.Decimal("annual_value");
        c.Notes = raw.Text("notes");
    }

    protected override void Deactivate(BillingContract c) => c.Active = false;

    // As the billing contracts export words Uptick's codes ("F" -> "Fixed"); an unknown code is kept as it is
    private static readonly Dictionary<string, string> TypeLabels = new() { ["F"] = "Fixed" };
    private static readonly Dictionary<string, string> FrequencyLabels = new()
    {
        ["W"] = "Weekly", ["M"] = "Monthly", ["Q"] = "Quarterly", ["H"] = "Half-yearly", ["A"] = "Annually", ["Y"] = "Annually"
    };
    private static readonly Dictionary<string, string> InvoiceModeLabels = new() { ["A"] = "Advance", ["R"] = "Arrears" };

    internal static string? Label(string? code, Dictionary<string, string> labels) =>
        code == null ? null : RoutineServiceTypeMapper.Cap(labels.GetValueOrDefault(code.ToUpperInvariant()) ?? code, 50);
}

// Uptick's billingcontractlineitems -> BillingContractLineItem, on its contract, with its product by name. A line
// the line items export placed (ExternalId = its id) is matched and updated.
public class BillingContractLineItemMapper : UptickEntityMapper<BillingContractLineItem>
{
    private Dictionary<long, int> _contracts = new();
    private Dictionary<long, string> _products = new();

    public BillingContractLineItemMapper(AppDbContext db) : base(db) { }

    public override string Resource => "billingcontractlineitems";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _contracts = (await Db.BillingContracts.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Id);
        _products = (await Db.Products.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Name }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Name);
    }

    protected override async Task<Dictionary<long, BillingContractLineItem>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.BillingContractLineItems
            .Where(l => (l.UptickId != null && ids.Contains(l.UptickId.Value)) || (l.ExternalId != null && texts.Contains(l.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), l => l.UptickId, l => l.ExternalId);
    }

    protected override BillingContractLineItem Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(BillingContractLineItem l, UptickRaw raw)
    {
        if (raw.Rel("billingcontract") is not { } contractId) throw new UptickMapException("It has no billing contract.");
        if (!_contracts.TryGetValue(contractId, out var contract)) throw new UptickMapException($"Its billing contract ({contractId}) isn't in FyreApp.");

        l.UptickId = raw.Id;
        l.ExternalId = raw.Id.ToString();
        l.BillingContractId = contract;
        l.ContractExternalId = contractId.ToString();
        l.Product = RoutineServiceTypeMapper.Cap(raw.Rel("product") is { } p ? _products.GetValueOrDefault(p) : null, 300);
        l.Description = raw.Text("description");
        l.UnitPrice = raw.Decimal("unit_price");
        l.Type = BillingContractMapper.Label(raw.Text("type"), LineTypeLabels);
        l.Visible = raw.Bool("visible") ?? true;
    }

    // A line gone from Uptick is gone from its contract
    protected override void Deactivate(BillingContractLineItem l) => Db.BillingContractLineItems.Remove(l);

    private static readonly Dictionary<string, string> LineTypeLabels = new() { ["V"] = "Per visit" };
}
