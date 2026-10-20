using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's clients -> Client. Fields the API doesn't have (report address and organisation) are kept.
public class ClientMapper : UptickEntityMapper<Client>
{
    private Dictionary<long, int> _cards = new();

    public ClientMapper(AppDbContext db) : base(db) { }

    public override string Resource => "clients";

    public override async Task BeginAsync(CancellationToken ct) => _cards = await BillingCardMapper.IdsAsync(Db, ct);

    protected override async Task<Dictionary<long, Client>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Clients
            .Where(c => c.UptickId != null && ids.Contains(c.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, c => c.UptickId);
    }

    protected override Client Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Client c, UptickRaw raw)
    {
        c.UptickId = raw.Id;
        c.IsPlaceholder = false;
        c.Name = raw.Text("name", 200) ?? "(no name)";
        c.Created = raw.Time("created") ?? c.Created;
        if (c.Created == default) c.Created = DateTime.UtcNow;
        c.Updated = raw.Time("updated");
        c.Active = raw.Bool("is_active") ?? true;
        c.BillingCardId = raw.Rel("billingcard") is { } card && _cards.TryGetValue(card, out var cardId) ? cardId : null;

        c.Reference = raw.Text("ref");
        c.PrimarySector = raw.Text("sector");
        c.Notes = raw.Text("notes");
        c.BillingRequirements = raw.Text("billing_requirements");

        c.PrimaryContactName = raw.Text("contact_name", 200);
        c.PrimaryContactEmail = raw.Text("contact_email", 320);
        c.PrimaryContactCcEmail = raw.Text("contact_email_cc", 320);
        c.PrimaryContactMobile = raw.Text("contact_mobile", 200);
        c.PrimaryContactPhone = raw.Text("contact_phone_bh");
        c.PrimaryContactAfterHours = raw.Text("contact_phone_ah");
        c.PrimaryContactAddress = raw.Text("contact_address", 320);

        c.BillingName = raw.Text("billing_organisation", 200);
        c.BillingAttentionTo = raw.Text("billing_attention", 200);
        c.BillingEmail = raw.Text("billing_email_to", 320);
        c.BillingCcEmail = raw.Text("billing_email_cc", 320);
        c.BillingAddress = raw.Text("billing_address", 320);

        c.ReportAttentionTo = raw.Text("report_attention");
        c.ReportEmail = raw.Text("report_email_to");
        c.ReportCcEmail = raw.Text("report_email_cc");
    }

    protected override void Deactivate(Client c) => c.Active = false;
}
