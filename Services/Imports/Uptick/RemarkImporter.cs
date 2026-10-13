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
        var taskIds = await LoadTaskIdsByExternalIdAsync(ct);
        var assetsByExt = (await Db.Assets.AsNoTracking()
                .Where(a => a.ExternalId != null)
                .Select(a => new { a.Id, a.SiteId, a.ExternalId })
                .ToListAsync(ct))
            .ToDictionary(a => a.ExternalId!, a => (a.Id, a.SiteId), StringComparer.OrdinalIgnoreCase);
        // Uptick remark types (reference data), by Uptick ID
        var remarkTypeIds = await Db.RemarkTypes.AsNoTracking()
            .Where(r => r.ExternalId != null)
            .ToDictionaryAsync(r => r.ExternalId!, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);
        var existing = ByExternalId(await Db.Defects.AsNoTracking()
            .Where(d => d.ExternalId != null)
            .Select(d => new ImportedRecord(d.Id, d.ExternalId!, d.UptickData))
            .ToListAsync(ct));

        await AddPlaceholderSitesAsync(rows.Where(r => NewOrChanged(r, "ID", existing)), sitesByRef, "Property Name", dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<Defect>();
        var toUpdate = new Dictionary<int, Action<Defect>>();
        var unlinkedAssets = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            var action = Claim(id, existing, seen, row, ctx, out var existingId);
            if (action == RowAction.Skip) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Missing property ref", id!, row.RowNumber, "Remark has no Property Ref. Row skipped.");
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

            int? remarkTypeId = remarkTypeIds.TryGetValue(row.Get("Remark Type ID") ?? "", out var rt) ? rt : null;

            if (action == RowAction.Create)
            {
                var defect = new Defect { ExternalId = id };
                Map(defect, row, siteId, assetId, remarkTypeId, taskIds);
                toCreate.Add(defect);
            }
            else
            {
                toUpdate[existingId] = d => Map(d, row, siteId, assetId, remarkTypeId, taskIds);
            }
        }

        ctx.Result.Created = toCreate.Count;
        ctx.Result.Updated = toUpdate.Count;
        if (unlinkedAssets > 0)
            ctx.Result.Notes.Add($"{unlinkedAssets} remark(s) reference an asset that isn't imported; they're attached to the property only. Import assets first to link them.");
        NoteNotInExport(ctx, existing, rows, "ID", "remarks");

        if (dryRun) return;

        await ApplyUpdatesAsync(Db.Defects, toUpdate, d => d.Id, ct);
        Db.Defects.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // Everything a remark takes from its Uptick row, for new and updated remarks alike
    private static void Map(Defect d, ImportRow row, int siteId, int? assetId, int? remarkTypeId, IReadOnlyDictionary<string, int> taskIds)
    {
        d.UptickData = row.ToJson();
        d.SiteId = siteId;
        d.AssetId = assetId;
        d.RemarkType = row.Get("Remark Type");
        d.RemarkTypeId = remarkTypeId;
        d.Status = row.Get("Status");
        d.Severity = row.GetInt("Severity");
        d.SeverityLabel = row.Get("Severity Display");
        d.Description = row.Get("Description");
        d.Resolution = row.Get("Resolution");
        d.Notes = row.Get("Notes");
        d.Location = row.Get("Location", "Asset Location");
        d.Active = row.GetBool("Active") ?? true;
        d.RaisedUtc = row.GetTimestampUtc("Created");
        d.RaisedOnTaskRef = row.Get("Created on Task Ref");
        d.QuoteRef = row.Get("Quote Ref");
        d.QuoteStatus = row.Get("Quote Status");
        d.RepairTaskRef = row.Get("Repair Task Ref");
        d.RepairTaskStatus = row.Get("Repair Task Status");
        d.RepairTaskExternalId = row.Get("Repair Task ID");
        // Linked now when the task is already imported; otherwise the tasks import links it
        d.RepairTaskId = d.RepairTaskExternalId != null && taskIds.TryGetValue(d.RepairTaskExternalId, out var taskId) ? taskId : null;
    }
}
