using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Asset type variants" export -> AssetTypeVariant (reference data), under its asset type.
// Also links already-imported assets whose type + variant text match.
public class AssetTypeVariantImporter : UptickImporter
{
    public AssetTypeVariantImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.AssetTypeVariants;
    public override string DisplayName => "Asset type variants";
    public override string[] SignatureHeaders => new[] { "Name", "Asset Type ID", "Default Replacement Product" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var types = await Db.AssetTypes.ToListAsync(ct);
        var typesByExt = types.Where(t => t.ExternalId != null).ToDictionary(t => t.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var typesByName = types.GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var variants = await Db.AssetTypeVariants.Include(v => v.AssetType).ToListAsync(ct);
        var byExt = variants.Where(v => v.ExternalId != null).ToDictionary(v => v.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var byTypeAndName = variants.GroupBy(v => Key(v.AssetType.Name, v.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;
        var newTypes = new List<string>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, new HashSet<string>(), seen, row, ctx)) continue;

            // Asset type by Uptick ID, then name; created by name if neither (as the assets import does)
            var typeName = row.Get("Asset Type") ?? "(no type)";
            var type = typesByExt.GetValueOrDefault(row.Get("Asset Type ID") ?? "") ?? typesByName.GetValueOrDefault(typeName);
            if (type == null)
            {
                type = new AssetType { Name = typeName };
                typesByName[typeName] = type;
                newTypes.Add(typeName);
                if (!dryRun) Db.AssetTypes.Add(type);
            }

            var name = row.Get("Name") ?? $"Variant {id}";
            var variant = byExt.GetValueOrDefault(id!) ?? byTypeAndName.GetValueOrDefault(Key(type.Name, name));
            if (variant == null)
            {
                variant = new AssetTypeVariant();
                byTypeAndName[Key(type.Name, name)] = variant;
                if (!dryRun) Db.AssetTypeVariants.Add(variant);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            variant.ExternalId = id;
            variant.AssetType = type;
            variant.Name = name;
            variant.DefaultReplacementProduct = row.Get("Default Replacement Product");
            variant.Active = row.GetBool("Active") ?? true;
            variant.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "variants");
        if (newTypes.Count > 0)
            ctx.Result.Notes.Add($"Asset types not imported yet {(dryRun ? "will be" : "were")} created by name: {string.Join(", ", newTypes.Distinct().OrderBy(n => n))}. Import the asset types export to fill them in.");

        if (dryRun) return;
        await Db.SaveChangesAsync(ct);

        // Link assets imported earlier: same asset type and variant text
        var lookup = await Db.AssetTypeVariants
            .Select(v => new { v.Id, v.AssetTypeId, v.Name })
            .ToListAsync(ct);
        var variantIds = lookup.GroupBy(v => (v.AssetTypeId, v.Name.Trim().ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var assets = await Db.Assets
            .Include(a => a.AssetTypes)
            .Where(a => a.AssetTypeVariantId == null && a.Variant != null)
            .ToListAsync(ct);
        var linked = 0;
        foreach (var asset in assets)
        {
            var variantName = asset.Variant!.Trim().ToLowerInvariant();
            foreach (var t in asset.AssetTypes)
            {
                if (variantIds.TryGetValue((t.Id, variantName), out var variantId))
                {
                    asset.AssetTypeVariantId = variantId;
                    linked++;
                    break;
                }
            }
        }

        if (linked > 0)
        {
            ctx.Result.Notes.Add($"{linked} existing asset{(linked == 1 ? "" : "s")} were linked to their variant.");
            await Db.SaveChangesAsync(ct);
        }
    }

    private static string Key(string typeName, string variantName) => $"{typeName.Trim()}|{variantName.Trim()}";
}
