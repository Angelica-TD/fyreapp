using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Property contacts" export -> SiteContact, matched to Site on "Property Ref".
public class PropertyContactImporter : UptickImporter
{
    public PropertyContactImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.PropertyContacts;
    public override string DisplayName => "Property contacts";
    public override string[] SignatureHeaders => new[] { "Property Ref", "Contact Name", "Role", "Report req" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var sitesByRef = await LoadSiteIdsByRefAsync(ct);
        var existing = ByExternalId(await Db.SiteContacts.AsNoTracking()
            .Where(c => c.ExternalId != null)
            .Select(c => new ImportedRecord(c.Id, c.ExternalId!, c.UptickData))
            .ToListAsync(ct));

        await AddPlaceholderSitesAsync(rows.Where(r => NewOrChanged(r, "ID", existing)), sitesByRef, "Property Name", dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<SiteContact>();
        var toUpdate = new Dictionary<int, Action<SiteContact>>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            var action = Claim(id, existing, seen, row, ctx, out var existingId);
            if (action == RowAction.Skip) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Missing property ref", id!, row.RowNumber, "Contact has no Property Ref. Row skipped.");
                continue;
            }

            if (action == RowAction.Create)
            {
                var contact = new SiteContact { ExternalId = id };
                Map(contact, row, siteId);
                toCreate.Add(contact);
            }
            else
            {
                toUpdate[existingId] = c => Map(c, row, siteId);
            }
        }

        ctx.Result.Created = toCreate.Count;
        ctx.Result.Updated = toUpdate.Count;
        NoteNotInExport(ctx, existing, rows, "ID", "contacts");
        if (dryRun) return;

        await ApplyUpdatesAsync(Db.SiteContacts, toUpdate, c => c.Id, ct);
        Db.SiteContacts.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // Everything a contact takes from its Uptick row, for new and updated contacts alike.
    // Imported as is, even with no name/email/mobile (Uptick keeps those too).
    private static void Map(SiteContact c, ImportRow row, int siteId)
    {
        var email = row.Get("Email");
        var mobile = row.Get("Mobile");

        c.UptickData = row.ToJson();
        c.SiteId = siteId;
        c.Name = row.Get("Contact Name") ?? row.Get("Organisation") ?? email ?? mobile ?? "";
        c.Organisation = row.Get("Organisation");
        c.Email = email;
        c.Mobile = mobile;
        c.BusinessHours = row.Get("Business Hours");
        c.Role = row.Get("Role");
        c.InvoiceRequired = row.GetBool("Invoice req") ?? false;
        c.ReportRequired = row.GetBool("Report req") ?? false;
        c.QuoteRequired = row.GetBool("Quote req") ?? false;
        c.NotificationRequired = row.GetBool("Notification req") ?? false;
        c.Active = row.GetBool("Active") ?? true;
    }
}
