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
        var existing = ByExternalId(await Db.Assets.AsNoTracking()
            .Where(a => a.ExternalId != null)
            .Select(a => new ImportedRecord(a.Id, a.ExternalId!, a.UptickData))
            .ToListAsync(ct));

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

        await AddPlaceholderSitesAsync(rows.Where(r => NewOrChanged(r, "ID", existing)), sitesByRef, "Property", dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<Asset>();
        var toUpdate = new Dictionary<int, Action<Asset>>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            var action = Claim(id, existing, seen, row, ctx, out var existingId);
            if (action == RowAction.Skip) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Missing property ref", id!, row.RowNumber, "Asset has no Property Ref. Row skipped.");
                continue;
            }

            // The asset type, created when FyreApp doesn't have it yet
            var typeName = row.Get("Type");
            AssetType? type = null;
            if (typeName != null && !types.TryGetValue(typeName, out type))
            {
                types[typeName] = type = new AssetType { Name = typeName };
                newTypes.Add(typeName);
            }
            var variant = row.Get("Variant");
            int? variantId = typeName != null && variant != null && variantIds.TryGetValue($"{typeName}|{variant}", out var v) ? v : null;

            if (action == RowAction.Create)
            {
                var asset = new Asset { ExternalId = id };
                Map(asset, row, siteId, type, variantId);
                toCreate.Add(asset);
            }
            else
            {
                toUpdate[existingId] = a => Map(a, row, siteId, type, variantId);
            }
        }

        ctx.Result.Created = toCreate.Count;
        ctx.Result.Updated = toUpdate.Count;
        if (newTypes.Count > 0)
            ctx.Result.Notes.Add($"New asset types {(dryRun ? "to create" : "created")}: {string.Join(", ", newTypes.OrderBy(t => t))}.");
        NoteNotInExport(ctx, existing, rows, "ID", "assets");

        if (dryRun) return;

        await ApplyUpdatesAsync(Db.Assets.Include(a => a.AssetTypes), toUpdate, a => a.Id, ct);
        Db.Assets.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // Everything an asset takes from its Uptick row, for new and updated assets alike
    private static void Map(Asset a, ImportRow row, int siteId, AssetType? type, int? variantId)
    {
        var typeName = row.Get("Type");
        var variant = row.Get("Variant");
        var name = row.Get("Label") ?? string.Join(" ", new[] { typeName, variant }.Where(s => s != null));
        if (string.IsNullOrWhiteSpace(name))
            name = $"Asset {row.Get("Ref") ?? row.Get("ID")}";

        a.UptickData = row.ToJson();
        a.SiteId = siteId;
        a.Name = name;
        a.Ref = row.Get("Ref");
        a.Location = row.Get("Location");
        a.Barcode = row.Get("Barcode");
        a.Variant = variant;
        a.Make = row.Get("Make");
        a.Model = row.Get("Model");
        a.Size = row.Get("Size");
        a.Compliance = row.Get("Compliant");
        a.IsActive = row.GetBool("Is Active") ?? true;
        a.BaseDate = row.GetDate("Base Date");
        a.InstallationDate = row.GetDate("Installation Date");
        a.LastServiceDate = row.GetDate("Last Service Date");

        // Uptick has one type per asset: a changed type replaces the old one
        if (type != null && !a.AssetTypes.Contains(type))
        {
            a.AssetTypes.Clear();
            a.AssetTypes.Add(type);
        }
        if (type != null)
            a.AssetTypeVariantId = variantId;
    }
}
