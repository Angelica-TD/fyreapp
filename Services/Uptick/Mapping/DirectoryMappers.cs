using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's groups -> SecurityGroup (their names split users' "Security Groups").
public class SecurityGroupMapper : UptickEntityMapper<SecurityGroup>
{
    public SecurityGroupMapper(AppDbContext db) : base(db) { }

    public override string Resource => "groups";

    protected override async Task<Dictionary<long, SecurityGroup>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.SecurityGroups
            .Where(g => g.UptickId != null && ids.Contains(g.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, g => g.UptickId);
    }

    protected override SecurityGroup Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(SecurityGroup g, UptickRaw raw)
    {
        g.UptickId = raw.Id;
        g.Name = raw.Text("name", 200) ?? $"Group {raw.Id}";
    }

    // A group gone from Uptick no longer splits anyone's groups
    protected override void Deactivate(SecurityGroup g) => Db.SecurityGroups.Remove(g);
}

// Uptick's contractors -> Contractor, with its category by name. Tags, skills and service areas aren't in the API,
// so the contractors export's are kept.
public class ContractorMapper : UptickEntityMapper<Contractor>
{
    private Dictionary<long, string> _categories = new();

    public ContractorMapper(AppDbContext db) : base(db) { }

    public override string Resource => "contractors";

    public override async Task BeginAsync(CancellationToken ct) =>
        _categories = await RoutineServiceTypeMapper.RawNamesAsync(Db, "contractorcategories", ct);

    protected override async Task<Dictionary<long, Contractor>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Contractors
            .Where(c => c.UptickId != null && ids.Contains(c.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, c => c.UptickId);
    }

    protected override Contractor Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Contractor c, UptickRaw raw)
    {
        c.UptickId = raw.Id;
        c.Name = raw.Text("name", 200) ?? $"Contractor {raw.Id}";
        c.PrimaryContact = raw.Text("primary_contact", 200);
        c.Email = raw.Text("email", 200);
        c.EmailCc = raw.Text("email_cc", 1000);
        c.Phone = raw.Text("contact_bh", 100);
        c.Mobile = raw.Text("contact_mobile", 100);
        c.Category = RoutineServiceTypeMapper.Cap(raw.Rel("category") is { } cat ? _categories.GetValueOrDefault(cat) : null, 100);
        c.Address = raw.Text("address", 500);
        c.Starred = raw.Bool("is_starred") ?? false;
        c.Active = raw.Bool("is_active") ?? true;
        c.CreatedUtc = raw.Time("created");
        c.UpdatedUtc = raw.Time("updated");
    }

    protected override void Deactivate(Contractor c) => c.Active = false;
}

// Uptick's suppliers -> Supplier, with its accounting organisation by name.
public class SupplierMapper : UptickEntityMapper<Supplier>
{
    private Dictionary<long, string> _organisations = new();

    public SupplierMapper(AppDbContext db) : base(db) { }

    public override string Resource => "suppliers";

    public override async Task BeginAsync(CancellationToken ct) =>
        _organisations = await RoutineServiceTypeMapper.RawNamesAsync(Db, "accountingorganisations", ct);

    protected override async Task<Dictionary<long, Supplier>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Suppliers
            .Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, s => s.UptickId);
    }

    protected override Supplier Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Supplier s, UptickRaw raw)
    {
        s.UptickId = raw.Id;
        s.Name = raw.Text("name", 200) ?? $"Supplier {raw.Id}";
        s.Ref = raw.Text("ref", 50);
        s.BusinessNumber = raw.Text("abn", 50);
        s.PaymentTerms = raw.Text("payment_terms", 100);
        s.PaymentDay = raw.Int("payment_day")?.ToString();
        s.AccountsContactName = raw.Text("accounts_name", 200);
        s.AccountsEmail = raw.Text("accounts_email", 200);
        s.PurchasingContactName = raw.Text("purchasing_name", 200);
        s.PurchasingEmail = raw.Text("purchasing_email", 200);
        s.AccountingOrganisation = RoutineServiceTypeMapper.Cap(
            raw.Rel("accounting_organisation") is { } o ? _organisations.GetValueOrDefault(o) : null, 200);
        s.Active = raw.Bool("is_active") ?? true;
        s.CreatedUtc = raw.Time("created");
        s.UpdatedUtc = raw.Time("updated");
    }

    protected override void Deactivate(Supplier s) => s.Active = false;
}

// Uptick's accreditations -> Accreditation, linked to the technician who holds it, its type by name.
public class AccreditationMapper : UptickEntityMapper<Accreditation>
{
    private Dictionary<long, (int Id, string Name)> _users = new();
    private Dictionary<long, string?> _properties = new();
    private Dictionary<long, string> _types = new();

    public AccreditationMapper(AppDbContext db) : base(db) { }

    public override string Resource => "accreditations";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Id, u.Name }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => (u.Id, u.Name));
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Ref }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Ref);
        _types = await RoutineServiceTypeMapper.RawNamesAsync(Db, "accreditationtypes", ct);
    }

    protected override async Task<Dictionary<long, Accreditation>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Accreditations
            .Where(a => a.UptickId != null && ids.Contains(a.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, a => a.UptickId);
    }

    protected override Accreditation Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Accreditation a, UptickRaw raw)
    {
        (int Id, string Name)? user = raw.Rel("technician") is { } t && _users.TryGetValue(t, out var found) ? found : null;
        if (raw.Rel("technician") is { } missing && user == null) throw new UptickMapException($"Its technician ({missing}) isn't in FyreApp.");

        a.UptickId = raw.Id;
        a.UptickUserId = user?.Id;
        a.Technician = RoutineServiceTypeMapper.Cap(user?.Name, 200);
        a.Type = RoutineServiceTypeMapper.Cap(raw.Rel("accreditationtype") is { } type ? _types.GetValueOrDefault(type) : null, 200);
        a.Number = raw.Text("number", 500);
        a.Expiry = raw.Date("expiry");
        a.Status = raw.Text("status", 30);
        a.PropertyRef = RoutineServiceTypeMapper.Cap(raw.Rel("property") is { } p ? _properties.GetValueOrDefault(p) : null, 64);
    }

    // An accreditation gone from Uptick is no longer held
    protected override void Deactivate(Accreditation a) => Db.Accreditations.Remove(a);
}
