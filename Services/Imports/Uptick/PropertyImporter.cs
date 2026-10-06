using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Properties" export -> Site. Client matched on Client.ExternalId ("Client ID"), then client name.
// An existing site with the same client + name but no ExternalId is linked rather than duplicated.
// Placeholder sites (created by other imports) with the same ref are filled in.
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
            .Select(s => new { s.Id, s.ClientId, s.Name, s.ExternalId, s.IsPlaceholder })
            .ToListAsync(ct);

        // Placeholders aren't "already imported": their row fills them in
        var existingRefs = sites
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalId) && !s.IsPlaceholder)
            .Select(s => s.ExternalId!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var placeholderIdsByRef = sites
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalId) && s.IsPlaceholder)
            .ToDictionary(s => s.ExternalId!.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);

        // Unlinked sites that a row could claim by client + name
        var unlinkedByName = sites
            .Where(s => string.IsNullOrWhiteSpace(s.ExternalId))
            .GroupBy(s => (s.ClientId, TabularFileReader.NormalizeHeader(s.Name)))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());

        await AddPlaceholderClientsAsync(rows.Where(r => !existingRefs.Contains(r.Get("Ref") ?? "")),
            clientsByExt, clientsByName, dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<Site>();
        var toLink = new Dictionary<int, string>();
        var toFill = new Dictionary<int, Site>();

        foreach (var row in rows)
        {
            var propertyRef = row.Get("Ref");
            if (!ClaimExternalId(propertyRef, existingRefs, seen, row, ctx)) continue;

            var clientExt = row.Get("Client ID");
            var clientName = row.Get("Client");
            int clientId;
            if (clientExt != null && clientsByExt.TryGetValue(clientExt, out var byExt)) clientId = byExt;
            else if (clientName != null && clientsByName.TryGetValue(clientName, out var byName)) clientId = byName;
            else
            {
                ctx.Skip("Missing client", propertyRef!, row.RowNumber, "Property has no Client ID or Client. Row skipped.");
                continue;
            }

            var name = row.Get("Name") ?? propertyRef!;

            var site = new Site
            {
                ClientId = clientId,
                Name = name,
                ExternalId = propertyRef,
                UptickData = row.ToJson(),
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

            if (placeholderIdsByRef.TryGetValue(propertyRef!, out var placeholderId))
            {
                toFill[placeholderId] = site;
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

            toCreate.Add(site);
        }

        ctx.Result.Created = toCreate.Count;
        if (toLink.Count > 0)
            ctx.Result.Notes.Add($"{toLink.Count} existing propert{(toLink.Count == 1 ? "y" : "ies")} matched by client + name {(dryRun ? "will be" : "were")} linked to their Uptick ref.");
        if (toFill.Count > 0)
            ctx.Result.Notes.Add($"{toFill.Count} placeholder propert{(toFill.Count == 1 ? "y" : "ies")} from earlier imports {(dryRun ? "will be" : "were")} filled in.");

        if (dryRun) return;

        if (toLink.Count > 0)
        {
            var ids = toLink.Keys.ToList();
            foreach (var s in await Db.Sites.Where(s => ids.Contains(s.Id)).ToListAsync(ct))
                s.ExternalId = toLink[s.Id];
        }

        if (toFill.Count > 0)
        {
            var ids = toFill.Keys.ToList();
            foreach (var s in await Db.Sites.Where(s => ids.Contains(s.Id)).ToListAsync(ct))
            {
                var from = toFill[s.Id];
                s.ClientId = from.ClientId;
                s.Name = from.Name;
                s.UptickData = from.UptickData;
                s.AddressDisplay = from.AddressDisplay;
                s.AddressLine1 = from.AddressLine1;
                s.Suburb = from.Suburb;
                s.State = from.State;
                s.Postcode = from.Postcode;
                s.Active = from.Active;
                s.IsPlaceholder = false;
            }
        }

        Db.Sites.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // A property can belong to a client that isn't in the clients export (usually archived in Uptick).
    // Create an inactive placeholder client from what the property row knows, instead of skipping it.
    private async Task AddPlaceholderClientsAsync(IEnumerable<ImportRow> rows,
        Dictionary<string, int> clientsByExt, Dictionary<string, int> clientsByName,
        bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var placeholders = new List<Client>();

        foreach (var row in rows)
        {
            var clientExt = row.Get("Client ID");
            var clientName = row.Get("Client");
            if (clientExt == null && clientName == null) continue;
            if (clientExt != null ? clientsByExt.ContainsKey(clientExt) : clientsByName.ContainsKey(clientName!)) continue;
            if (clientExt != null && placeholders.Any(c => c.ExternalId == clientExt)) continue;
            if (clientExt == null && placeholders.Any(c => c.ExternalId == null && c.Name == clientName)) continue;

            var client = new Client
            {
                ExternalId = clientExt,
                Name = clientName ?? $"Client {clientExt}",
                PrimaryContactName = row.Get("Client Primary Contact Name"),
                PrimaryContactEmail = row.Get("Client Primary Contact Email"),
                Active = false,
                IsPlaceholder = true,
                Created = DateTime.UtcNow
            };
            placeholders.Add(client);

            ctx.Issue("Placeholder client", clientExt ?? clientName!, row.RowNumber,
                $"Client '{client.Name}' isn't in FyreApp (probably archived in Uptick, so not in the clients export). " +
                "Created as an inactive placeholder; import a clients export that includes it to fill it in.");
        }

        if (placeholders.Count == 0) return;

        ctx.Result.Notes.Add(
            $"{placeholders.Count} client{(placeholders.Count == 1 ? "" : "s")} referenced here {(placeholders.Count == 1 ? "isn't" : "aren't")} in FyreApp " +
            $"and {(dryRun ? "will be" : "were")} created as inactive placeholders. Import a clients export that includes archived clients to fill them in.");

        if (!dryRun)
        {
            Db.Clients.AddRange(placeholders);
            await Db.SaveChangesAsync(ct);
        }

        var tempId = -1;
        foreach (var c in placeholders)
        {
            var id = dryRun ? tempId-- : c.Id;
            if (c.ExternalId != null) clientsByExt[c.ExternalId] = id;
            else clientsByName[c.Name] = id;
        }
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
        if (s.State?.Length > 100) return "State";
        if (s.Postcode?.Length > 10) return "Postcode";
        return null;
    }
}
