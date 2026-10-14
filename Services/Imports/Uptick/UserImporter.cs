using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Users" export -> UptickUser. A record of who's who in Uptick; it doesn't create FyreApp logins.
// Refreshed from each export, like reference data.
public class UserImporter : UptickImporter
{
    public UserImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Users;
    public override string DisplayName => "Users";
    public override string[] SignatureHeaders => new[] { "Date joined", "Last login", "Security Groups" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.UptickUsers.Where(u => u.ExternalId != null).ToListAsync(ct))
            .ToDictionary(u => u.ExternalId!, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var user))
            {
                user = new UptickUser { ExternalId = id };
                byExt[id!] = user;
                if (!dryRun) Db.UptickUsers.Add(user);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            user.Name = Cap(row.Get("Name"), 200) ?? $"User {id}";
            user.Email = Cap(row.Get("Email"), 200);
            // The export has two "License" columns: the licence type, then the trade licence number
            user.LicenseType = Cap(row.Get("License"), 20);
            user.Licence = Cap(row.Get("License (2)"), 200);
            user.SecurityGroups = Cap(row.Get("Security Groups"), 500);
            user.ServiceGroup = Cap(row.Get("Service Group"), 200);
            user.HomeBranch = Cap(row.Get("Home Branch"), 200);
            user.AllowedBranches = Cap(row.Get("Allowed Branches"), 500);
            user.CostRate = row.GetDecimal("Cost Rate");
            user.SellRate = row.GetDecimal("Sell Rate");
            user.JoinedUtc = row.GetTimestampUtc("Date joined");
            user.LastLoginUtc = row.GetTimestampUtc("Last login");
            user.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "users");
        ctx.Result.Notes.Add("Uptick users are kept as a record only; they don't get a FyreApp login.");

        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
