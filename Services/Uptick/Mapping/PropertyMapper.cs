using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's properties -> Property, under the client mapped from the property's client. A property the
// properties export placed (ExternalId = its ref) is matched and updated, placeholders filled in.
public class PropertyMapper : UptickEntityMapper<Property>
{
    private Dictionary<long, int> _clients = new();
    private Dictionary<long, int> _cards = new();

    public PropertyMapper(AppDbContext db) : base(db) { }

    public override string Resource => "properties";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _clients = (await Db.Clients.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Id);
        _cards = await BillingCardMapper.IdsAsync(Db, ct);
    }

    protected override async Task<Dictionary<long, Property>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var refs = batch.Select(r => r.Text("ref")).Where(r => r != null).ToList();
        var rows = await Db.Properties
            .Where(p => (p.UptickId != null && ids.Contains(p.UptickId.Value)) || (p.ExternalId != null && refs.Contains(p.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, r.Text("ref"))), p => p.UptickId, p => p.ExternalId);
    }

    protected override Property Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Property p, UptickRaw raw)
    {
        var client = raw.Rel("client");
        if (client == null) throw new UptickMapException("It has no client.");
        if (!_clients.TryGetValue(client.Value, out var clientId))
            throw new UptickMapException($"Its client ({client}) isn't in FyreApp.");

        var reference = raw.Text("ref", 64);
        p.UptickId = raw.Id;
        p.Ref = reference;
        p.ExternalId = reference;
        p.IsPlaceholder = false;
        p.ClientId = clientId;
        p.Name = raw.Text("name") ?? reference ?? $"Property {raw.Id}";
        p.Status = raw.Text("status", 20)?.ToUpperInvariant();
        p.Active = p.Status != "INACTIVE";
        p.Created = raw.Time("created");
        p.InactiveDate = raw.Time("inactive_date");

        var address = UptickRaw.Get(raw.A, "address");
        var display = Cap(StripCountry(UptickRaw.Str(address, "display")?.Trim()), 300);
        var placeId = Cap(UptickRaw.Str(address, "place_id")?.Trim(), 300);
        // A moved address with no place from Uptick needs looking up again
        p.GooglePlaceId = placeId ?? (string.Equals(p.AddressDisplay, display, StringComparison.Ordinal) ? p.GooglePlaceId : null);
        p.AddressDisplay = display;
        p.AddressLine1 = Cap(UptickRaw.Str(address, "streetline")?.Trim(), 200);
        p.Suburb = Cap(UptickRaw.Str(address, "city")?.Trim(), 80);
        p.State = Cap(UptickRaw.Str(address, "state")?.Trim(), 100);
        p.Postcode = Cap(UptickRaw.Str(address, "postal_code")?.Trim(), 10);
        var coords = UptickRaw.Get(raw.A, "coords");
        p.Latitude = UptickRaw.Double(coords, "lat");
        p.Longitude = UptickRaw.Double(coords, "lng");

        p.AccessSchedule = raw.Text("access_schedule", 20);
        p.AccessProcedure = raw.Text("access_procedure", 20);
        p.AccessCode = raw.Text("access_code", 100);
        p.AccessNote = raw.Text("access_note");
        p.InternalNote = raw.Text("internal_note");
        p.ServiceRequirements = raw.Text("service_requirements");
        p.SafetyRequirements = raw.Text("safety_requirements");
        p.BillingRequirements = raw.Text("billing_requirements");
        p.BillingCardId = raw.Rel("billingcard") is { } card && _cards.TryGetValue(card, out var cardId) ? cardId : null;
    }

    protected override void Deactivate(Property p) => p.Active = false;

    private static string? StripCountry(string? address) =>
        address != null && address.EndsWith(", Australia", StringComparison.OrdinalIgnoreCase)
            ? address[..^", Australia".Length]
            : address;

    private static string? Cap(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;
}
