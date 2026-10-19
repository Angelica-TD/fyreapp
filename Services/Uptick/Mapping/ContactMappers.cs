using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's propertycontacts -> PropertyContact, on the property mapped from the contact's property.
// (Uptick's "contacts" endpoint is a list of these, client contacts and clients' primary contacts together, so it
// isn't mapped itself.)
public class PropertyContactMapper : UptickEntityMapper<PropertyContact>
{
    private Dictionary<long, int> _properties = new();

    public PropertyContactMapper(AppDbContext db) : base(db) { }

    public override string Resource => "propertycontacts";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Id);
    }

    protected override async Task<Dictionary<long, PropertyContact>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.PropertyContacts
            .Where(c => (c.UptickId != null && ids.Contains(c.UptickId.Value)) || (c.ExternalId != null && texts.Contains(c.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), c => c.UptickId, c => c.ExternalId);
    }

    protected override PropertyContact Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(PropertyContact c, UptickRaw raw)
    {
        var property = raw.Rel("property");
        if (property == null) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(property.Value, out var propertyId))
            throw new UptickMapException($"Its property ({property}) isn't in FyreApp.");

        var email = raw.Text("email");
        var mobile = raw.Text("mobile");
        c.UptickId = raw.Id;
        c.ExternalId = raw.Id.ToString();
        c.PropertyId = propertyId;
        c.Name = raw.Text("name") ?? raw.Text("organisation") ?? email ?? mobile ?? "";
        c.Organisation = raw.Text("organisation");
        c.Email = email;
        c.Mobile = mobile;
        c.BusinessHours = raw.Text("phone_bh");
        c.Role = raw.Text("role");
        c.InvoiceRequired = raw.Bool("requires_invoice") ?? false;
        c.ReportRequired = raw.Bool("requires_report") ?? false;
        c.QuoteRequired = raw.Bool("requires_quote") ?? false;
        c.NotificationRequired = raw.Bool("requires_notification") ?? false;
        c.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(PropertyContact c) => c.Active = false;
}

// Uptick's clientcontacts -> ClientContact, on the client mapped from the contact's client
public class ClientContactMapper : UptickEntityMapper<ClientContact>
{
    private Dictionary<long, int> _clients = new();

    public ClientContactMapper(AppDbContext db) : base(db) { }

    public override string Resource => "clientcontacts";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _clients = (await Db.Clients.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Id);
    }

    protected override async Task<Dictionary<long, ClientContact>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.ClientContacts
            .Where(c => (c.UptickId != null && ids.Contains(c.UptickId.Value)) || (c.ExternalId != null && texts.Contains(c.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), c => c.UptickId, c => c.ExternalId);
    }

    protected override ClientContact Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(ClientContact c, UptickRaw raw)
    {
        var client = raw.Rel("client");
        if (client == null) throw new UptickMapException("It has no client.");
        if (!_clients.TryGetValue(client.Value, out var clientId))
            throw new UptickMapException($"Its client ({client}) isn't in FyreApp.");

        var email = raw.Text("email", 200);
        var mobile = raw.Text("mobile", 100);
        c.UptickId = raw.Id;
        c.ExternalId = raw.Id.ToString();
        c.ClientId = clientId;
        c.Name = raw.Text("name", 200) ?? email ?? mobile ?? "";
        c.Email = email;
        c.Mobile = mobile;
        c.Phone = raw.Text("phone", 100);
        c.Position = raw.Text("position", 200);
        c.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(ClientContact c) => c.Active = false;
}
