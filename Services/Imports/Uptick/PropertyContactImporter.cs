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
        var existing = (await Db.SiteContacts.AsNoTracking()
                .Where(c => c.ExternalId != null)
                .Select(c => c.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<SiteContact>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, existing, seen, row, ctx)) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Property not found", propertyRef ?? "(blank)", row.RowNumber,
                    $"No property with ref '{propertyRef}' ({row.Get("Property Name")}). Import properties first. Row skipped.");
                continue;
            }

            var name = row.Get("Contact Name") ?? row.Get("Organisation");
            var email = row.Get("Email");
            var mobile = row.Get("Mobile");
            if (name == null && email == null && mobile == null)
            {
                ctx.Skip("Empty contact", id!, row.RowNumber, "Contact has no name, organisation, email or mobile. Skipped.");
                continue;
            }

            toCreate.Add(new SiteContact
            {
                ExternalId = id,
                SiteId = siteId,
                Name = name ?? email ?? mobile!,
                Organisation = row.Get("Organisation"),
                Email = email,
                Mobile = mobile,
                BusinessHours = row.Get("Business Hours"),
                Role = row.Get("Role"),
                InvoiceRequired = row.GetBool("Invoice req") ?? false,
                ReportRequired = row.GetBool("Report req") ?? false,
                QuoteRequired = row.GetBool("Quote req") ?? false,
                NotificationRequired = row.GetBool("Notification req") ?? false,
                Active = row.GetBool("Active") ?? true
            });
        }

        ctx.Result.Created = toCreate.Count;
        if (dryRun) return;

        Db.SiteContacts.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }
}
