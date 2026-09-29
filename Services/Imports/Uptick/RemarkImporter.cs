using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Remarks" export -> Defect, matched to Site on "Property Ref" and to Asset on "Asset ID".
public class RemarkImporter : UptickImporter
{
    public RemarkImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Remarks;
    public override string DisplayName => "Remarks";
    public override string[] SignatureHeaders => new[] { "Remark Type", "Property Ref", "Severity", "Resolution" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var sitesByRef = await LoadSiteIdsByRefAsync(ct);
        var assetsByExt = (await Db.Assets.AsNoTracking()
                .Where(a => a.ExternalId != null)
                .Select(a => new { a.Id, a.SiteId, a.ExternalId })
                .ToListAsync(ct))
            .ToDictionary(a => a.ExternalId!, a => (a.Id, a.SiteId), StringComparer.OrdinalIgnoreCase);
        var existing = (await Db.Defects.AsNoTracking()
                .Where(d => d.ExternalId != null)
                .Select(d => d.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<Defect>();
        var unlinkedAssets = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, existing, seen, row, ctx)) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Property not found", propertyRef ?? "(blank)", row.RowNumber,
                    $"No property with ref '{propertyRef}' ({row.Get("Property Name")}). Import properties first. Row skipped.");
                continue;
            }

            int? assetId = null;
            var assetExt = row.Get("Asset ID");
            if (assetExt != null)
            {
                if (assetsByExt.TryGetValue(assetExt, out var asset) && asset.SiteId == siteId)
                    assetId = asset.Id;
                else
                    unlinkedAssets++;
            }

            toCreate.Add(new Defect
            {
                ExternalId = id,
                SiteId = siteId,
                AssetId = assetId,
                RemarkType = row.Get("Remark Type"),
                Status = row.Get("Status"),
                Severity = row.GetInt("Severity"),
                SeverityLabel = row.Get("Severity Display"),
                Description = row.Get("Description"),
                Resolution = row.Get("Resolution"),
                Notes = row.Get("Notes"),
                Location = row.Get("Location", "Asset Location"),
                Active = row.GetBool("Active") ?? true,
                RaisedUtc = row.GetTimestampUtc("Created"),
                RaisedOnTaskRef = row.Get("Created on Task Ref"),
                QuoteRef = row.Get("Quote Ref"),
                QuoteStatus = row.Get("Quote Status"),
                RepairTaskRef = row.Get("Repair Task Ref"),
                RepairTaskStatus = row.Get("Repair Task Status")
            });
        }

        ctx.Result.Created = toCreate.Count;
        if (unlinkedAssets > 0)
            ctx.Result.Notes.Add($"{unlinkedAssets} remark(s) reference an asset that isn't imported; they're attached to the property only. Import assets first to link them.");

        if (dryRun) return;

        Db.Defects.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }
}
