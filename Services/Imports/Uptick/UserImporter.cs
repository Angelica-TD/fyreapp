using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.Services.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Users" export -> UptickUser, the source of truth for who's who. Refreshed from each export, like
// reference data; FyreApp logins linked to these users then take their name and email from it. It doesn't
// create logins (an admin gives those from the Users page).
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
        if (dryRun)
        {
            ctx.Result.Notes.Add("FyreApp logins linked to these users will take their name and email from this export.");
            return;
        }

        await Db.SaveChangesAsync(ct);

        var sync = await UptickLoginSync.ApplyAsync(Db, ct);
        var tasksAssigned = await TechnicianMatch.AssignImportedTasksAsync(Db, ct);
        await Db.SaveChangesAsync(ct);
        if (tasksAssigned > 0)
            ctx.Result.Notes.Add($"{tasksAssigned} imported task{(tasksAssigned == 1 ? " was" : "s were")} assigned to the Uptick user Uptick names as the technician.");
        if (sync.Linked > 0)
            ctx.Result.Notes.Add($"{sync.Linked} FyreApp login{(sync.Linked == 1 ? " was" : "s were")} linked to the Uptick user with the same email.");
        if (sync.Updated > 0)
            ctx.Result.Notes.Add($"{sync.Updated} FyreApp login{(sync.Updated == 1 ? "" : "s")} took {(sync.Updated == 1 ? "its" : "their")} name and email from Uptick.");
        ctx.Result.Notes.AddRange(sync.Problems);
        ctx.Result.Notes.Add("Uptick users don't get a FyreApp login automatically; give one from the Users page.");
    }
}
