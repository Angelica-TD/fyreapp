using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Client contacts" export -> ClientContact, matched to Client on "Client ID".
// A contact whose client isn't in FyreApp (usually archived in Uptick) gets an inactive placeholder client.
public class ClientContactImporter : UptickImporter
{
    public ClientContactImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.ClientContacts;
    public override string DisplayName => "Client contacts";
    public override string[] SignatureHeaders => new[] { "Client ID", "Client Name", "Position", "Phone" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var clientsByExt = (await Db.Clients.AsNoTracking()
                .Where(c => c.ExternalId != null && c.ExternalId != "")
                .Select(c => new { c.Id, c.ExternalId })
                .ToListAsync(ct))
            .GroupBy(c => c.ExternalId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var existing = ByExternalId(await Db.ClientContacts.AsNoTracking()
            .Where(c => c.ExternalId != null)
            .Select(c => new ImportedRecord(c.Id, c.ExternalId!, c.UptickData))
            .ToListAsync(ct));

        await AddPlaceholderClientsAsync(rows.Where(r => NewOrChanged(r, "ID", existing)), clientsByExt, dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<ClientContact>();
        var toUpdate = new Dictionary<int, Action<ClientContact>>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            var action = Claim(id, existing, seen, row, ctx, out var existingId);
            if (action == RowAction.Skip) continue;

            var clientExt = row.Get("Client ID");
            if (clientExt == null || !clientsByExt.TryGetValue(clientExt, out var clientId))
            {
                ctx.Skip("Missing client", id!, row.RowNumber, "Contact has no Client ID. Row skipped.");
                continue;
            }

            if (action == RowAction.Create)
            {
                var contact = new ClientContact { ExternalId = id };
                Map(contact, row, clientId);
                toCreate.Add(contact);
            }
            else
            {
                toUpdate[existingId] = c => Map(c, row, clientId);
            }
        }

        ctx.Result.Created = toCreate.Count;
        ctx.Result.Updated = toUpdate.Count;
        NoteNotInExport(ctx, existing, rows, "ID", "client contacts");
        if (dryRun) return;

        await ApplyUpdatesAsync(Db.ClientContacts, toUpdate, c => c.Id, ct);
        Db.ClientContacts.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // Everything a contact takes from its Uptick row, for new and updated contacts alike.
    // Imported as is, even with no name (Uptick keeps those too).
    private static void Map(ClientContact c, ImportRow row, int clientId)
    {
        var email = row.Get("Email");
        var mobile = row.Get("Mobile");
        var phone = row.Get("Phone");

        c.UptickData = row.ToJson();
        c.ClientId = clientId;
        c.Name = Cut(row.Get("Name") ?? email ?? mobile ?? phone ?? "", 200);
        c.Email = Cut(email, 200);
        c.Mobile = Cut(mobile, 100);
        c.Phone = Cut(phone, 100);
        c.Position = Cut(row.Get("Position"), 200);
        c.Active = row.GetBool("Active") ?? true;
    }

    // Keeps the row; the full value stays in UptickData
    private static string? Cut(string? s, int max) => s == null || s.Length <= max ? s : s[..max];

    // A contact's client that isn't in FyreApp: an inactive placeholder (from the row's Client ID + name),
    // filled in when a clients export that includes it is imported
    private async Task AddPlaceholderClientsAsync(IEnumerable<ImportRow> rows, Dictionary<string, int> clientsByExt,
        bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var placeholders = new Dictionary<string, Client>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var clientExt = row.Get("Client ID");
            if (clientExt == null || clientsByExt.ContainsKey(clientExt) || placeholders.ContainsKey(clientExt)) continue;

            var client = new Client
            {
                ExternalId = clientExt,
                Name = row.Get("Client Name") ?? $"Client {clientExt}",
                Active = false,
                IsPlaceholder = true,
                Created = DateTime.UtcNow
            };
            placeholders[clientExt] = client;
            ctx.Issue("Placeholder client", clientExt, row.RowNumber,
                $"Client '{client.Name}' isn't in FyreApp (probably archived in Uptick, so not in the clients export). " +
                "Created as an inactive placeholder; import a clients export that includes it to fill it in.");
        }
        if (placeholders.Count == 0) return;

        ctx.Result.Notes.Add(
            $"{placeholders.Count} client(s) referenced here aren't in FyreApp and {(dryRun ? "will be" : "were")} created as inactive placeholders. " +
            "Import a clients export that includes archived clients to fill them in.");

        if (!dryRun)
        {
            Db.Clients.AddRange(placeholders.Values);
            await Db.SaveChangesAsync(ct);
        }

        var tempId = -1;
        foreach (var (ext, c) in placeholders)
            clientsByExt[ext] = dryRun ? tempId-- : c.Id;
    }
}
