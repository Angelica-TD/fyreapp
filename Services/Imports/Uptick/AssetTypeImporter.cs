using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Asset types" export -> AssetType (reference data). Matched on Uptick ID, then name, since the
// assets import creates asset types by name.
public class AssetTypeImporter : UptickImporter
{
    public AssetTypeImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.AssetTypes;
    public override string DisplayName => "Asset types";
    public override string[] SignatureHeaders => new[] { "Name", "Sequence Pattern", "Applicable Routines" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var types = await Db.AssetTypes.ToListAsync(ct);
        var byExt = types.Where(t => t.ExternalId != null).ToDictionary(t => t.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var byName = types.GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, new HashSet<string>(), seen, row, ctx)) continue;

            var name = row.Get("Name") ?? $"Asset type {id}";
            var type = byExt.GetValueOrDefault(id!) ?? byName.GetValueOrDefault(name);
            if (type == null)
            {
                type = new AssetType();
                byName[name] = type;
                if (!dryRun) Db.AssetTypes.Add(type);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            type.ExternalId = id;
            type.Name = name;
            type.Category = row.Get("Category");
            type.InspectionCriteria = row.Get("Inspection Criteria");
            type.SequenceGroup = row.Get("Sequence Group");
            type.Description = row.Get("Description");
            type.Active = row.GetBool("Active") ?? true;
            type.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "asset types");

        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
