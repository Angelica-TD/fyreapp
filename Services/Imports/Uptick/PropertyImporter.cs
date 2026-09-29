using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Properties" export -> Site. Client matched on Client.ExternalId ("Client ID"), then client name.
// An existing site with the same client + name but no ExternalId is linked rather than duplicated.
public class PropertyImporter : UptickImporter
{
    public PropertyImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Properties;
    public override string DisplayName => "Properties";
    public override string[] SignatureHeaders => new[] { "Ref", "Name", "Client ID", "Address Street Address" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var clients = await Db.Clients.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ExternalId })
            .ToListAsync(ct);

        var clientsByExt = clients
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalId))
            .ToDictionary(c => c.ExternalId!.Trim(), c => c.Id, StringComparer.OrdinalIgnoreCase);
        var clientsByName = clients
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var sites = await Db.Sites.AsNoTracking()
            .Select(s => new { s.Id, s.ClientId, s.Name, s.ExternalId })
            .ToListAsync(ct);

        var existingRefs = sites
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalId))
            .Select(s => s.ExternalId!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Unlinked sites that a row could claim by client + name
        var unlinkedByName = sites
            .Where(s => string.IsNullOrWhiteSpace(s.ExternalId))
            .GroupBy(s => (s.ClientId, TabularFileReader.NormalizeHeader(s.Name)))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<Site>();
        var toLink = new Dictionary<int, string>();

        foreach (var row in rows)
        {
            var propertyRef = row.Get("Ref");
            if (!ClaimExternalId(propertyRef, existingRefs, seen, row, ctx)) continue;

            var name = row.Get("Name");
            if (name == null)
            {
                ctx.Skip("Missing name", propertyRef!, row.RowNumber, "Property has no Name. Skipped.");
                continue;
            }

            var clientExt = row.Get("Client ID");
            var clientName = row.Get("Client");
            int clientId;
            if (clientExt != null && clientsByExt.TryGetValue(clientExt, out var byExt)) clientId = byExt;
            else if (clientName != null && clientsByName.TryGetValue(clientName, out var byName)) clientId = byName;
            else
            {
                ctx.Skip("Client not found", clientName ?? clientExt ?? "(blank)", row.RowNumber,
                    $"Client '{clientName}' (ID {clientExt}) isn't in FyreApp. Import clients first. Row skipped.");
                continue;
            }

            if (unlinkedByName.TryGetValue((clientId, TabularFileReader.NormalizeHeader(name)), out var matches) && matches.Count > 0)
            {
                // Claim one existing site per row, so two same-named Uptick properties don't link to the same site
                var siteId = matches[0];
                matches.RemoveAt(0);
                toLink[siteId] = propertyRef!;
                continue;
            }

            var site = new Site
            {
                ClientId = clientId,
                Name = name,
                ExternalId = propertyRef,
                AddressDisplay = StripCountry(row.Get("Address")),
                AddressLine1 = row.Get("Address Street Address"),
                Suburb = row.Get("Address City"),
                State = row.Get("Address State"),
                Postcode = row.Get("Address Postcode"),
                Active = !string.Equals(row.Get("Status"), "INACTIVE", StringComparison.OrdinalIgnoreCase)
            };

            var tooLong = TooLong(site);
            if (tooLong != null)
            {
                ctx.Skip("Value too long", propertyRef!, row.RowNumber, $"{tooLong} is longer than FyreApp allows. Row skipped.");
                continue;
            }

            toCreate.Add(site);
        }

        ctx.Result.Created = toCreate.Count;
        if (toLink.Count > 0)
            ctx.Result.Notes.Add($"{toLink.Count} existing propert{(toLink.Count == 1 ? "y" : "ies")} matched by client + name {(dryRun ? "will be" : "were")} linked to their Uptick ref.");

        if (dryRun) return;

        if (toLink.Count > 0)
        {
            var ids = toLink.Keys.ToList();
            foreach (var s in await Db.Sites.Where(s => ids.Contains(s.Id)).ToListAsync(ct))
                s.ExternalId = toLink[s.Id];
        }

        Db.Sites.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    private static string? StripCountry(string? address) =>
        address != null && address.EndsWith(", Australia", StringComparison.OrdinalIgnoreCase)
            ? address[..^", Australia".Length]
            : address;

    // Mirrors the [StringLength] limits on Site
    private static string? TooLong(Site s)
    {
        if (s.ExternalId?.Length > 64) return "Ref";
        if (s.AddressDisplay?.Length > 300) return "Address";
        if (s.AddressLine1?.Length > 200) return "Street address";
        if (s.Suburb?.Length > 80) return "City";
        if (s.State?.Length > 20) return "State";
        if (s.Postcode?.Length > 10) return "Postcode";
        return null;
    }
}
