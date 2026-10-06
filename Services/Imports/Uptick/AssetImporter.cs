using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Assets" export -> Asset, matched to Site on "Property Ref". "Type" becomes an AssetType.
public class AssetImporter : UptickImporter
{
    public AssetImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Assets;
    public override string DisplayName => "Assets";
    public override string[] SignatureHeaders => new[] { "Label", "Property Ref", "Guid", "Type", "Variant" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var sitesByRef = await LoadSiteIdsByRefAsync(ct);
        var existing = (await Db.Assets.AsNoTracking()
                .Where(a => a.ExternalId != null)
                .Select(a => a.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var types = (await Db.AssetTypes.ToListAsync(ct))
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var newTypes = new List<string>();

        // Uptick variants (reference data), by asset type name + variant name
        var variantIds = (await Db.AssetTypeVariants.AsNoTracking()
                .Select(v => new { v.Id, TypeName = v.AssetType.Name, v.Name })
                .ToListAsync(ct))
            .GroupBy(v => $"{v.TypeName.Trim()}|{v.Name.Trim()}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        await AddPlaceholderSitesAsync(rows.Where(r => !existing.Contains(r.Get("ID") ?? "")), sitesByRef, "Property", dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<Asset>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, existing, seen, row, ctx)) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Missing property ref", id!, row.RowNumber, "Asset has no Property Ref. Row skipped.");
                continue;
            }

            var typeName = row.Get("Type");
            var variant = row.Get("Variant");
            var name = row.Get("Label") ?? string.Join(" ", new[] { typeName, variant }.Where(s => s != null));
            if (string.IsNullOrWhiteSpace(name))
                name = $"Asset {row.Get("Ref") ?? id}";

            var asset = new Asset
            {
                ExternalId = id,
                UptickData = row.ToJson(),
                SiteId = siteId,
                Name = name,
                Ref = row.Get("Ref"),
                Location = row.Get("Location"),
                Barcode = row.Get("Barcode"),
                Variant = variant,
                Make = row.Get("Make"),
                Model = row.Get("Model"),
                Size = row.Get("Size"),
                Compliance = row.Get("Compliant"),
                IsActive = row.GetBool("Is Active") ?? true,
                BaseDate = row.GetDate("Base Date"),
                InstallationDate = row.GetDate("Installation Date"),
                LastServiceDate = row.GetDate("Last Service Date")
            };

            if (typeName != null)
            {
                if (!types.TryGetValue(typeName, out var type))
                {
                    types[typeName] = type = new AssetType { Name = typeName };
                    newTypes.Add(typeName);
                }
                asset.AssetTypes.Add(type);

                if (variant != null && variantIds.TryGetValue($"{typeName}|{variant}", out var variantId))
                    asset.AssetTypeVariantId = variantId;
            }

            toCreate.Add(asset);
        }

        ctx.Result.Created = toCreate.Count;
        if (newTypes.Count > 0)
            ctx.Result.Notes.Add($"New asset types {(dryRun ? "to create" : "created")}: {string.Join(", ", newTypes.OrderBy(t => t))}.");

        if (dryRun) return;

        Db.Assets.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }
}
