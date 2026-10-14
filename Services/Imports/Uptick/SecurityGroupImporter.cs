using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "groups" export -> SecurityGroup (reference data, refreshed from each export). Lets the Users page
// split a user's "Security Groups" back into group names.
public class SecurityGroupImporter : UptickImporter
{
    public SecurityGroupImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.SecurityGroups;
    public override string DisplayName => "Security groups";
    public override string[] SignatureHeaders => new[] { "id", "name" };

    // Just "id" and "name", which most exports have, so only a file with exactly those columns
    public override bool Recognises(IReadOnlySet<string> headers) =>
        headers.Count == 2 && base.Recognises(headers);

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.SecurityGroups.Where(g => g.ExternalId != null).ToListAsync(ct))
            .ToDictionary(g => g.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("id");
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var group))
            {
                group = new SecurityGroup { ExternalId = id };
                byExt[id!] = group;
                if (!dryRun) Db.SecurityGroups.Add(group);
                created++;
            }
            else updated++;

            if (!dryRun) group.Name = Cap(row.Get("name"), 200) ?? $"Group {id}";
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "security groups");
        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
