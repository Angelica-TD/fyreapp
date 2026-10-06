using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Remark types" export -> RemarkType (reference data). Also links already-imported defects
// through the "Remark Type ID" kept in their Uptick data.
public class RemarkTypeImporter : UptickImporter
{
    public RemarkTypeImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.RemarkTypes;
    public override string DisplayName => "Remark types";
    public override string[] SignatureHeaders => new[] { "Label", "Asset Type Tag", "Owner Responsible" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var typesByName = (await Db.AssetTypes.ToListAsync(ct))
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var byExt = (await Db.RemarkTypes.Where(r => r.ExternalId != null).ToListAsync(ct))
            .ToDictionary(r => r.ExternalId!, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;
        var newTypes = new List<string>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, new HashSet<string>(), seen, row, ctx)) continue;

            AssetType? assetType = null;
            var typeName = row.Get("Asset Type");
            if (typeName != null && !typesByName.TryGetValue(typeName, out assetType))
            {
                assetType = new AssetType { Name = typeName };
                typesByName[typeName] = assetType;
                newTypes.Add(typeName);
                if (!dryRun) Db.AssetTypes.Add(assetType);
            }

            if (!byExt.TryGetValue(id!, out var remarkType))
            {
                remarkType = new RemarkType();
                byExt[id!] = remarkType;
                if (!dryRun) Db.RemarkTypes.Add(remarkType);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            remarkType.ExternalId = id;
            remarkType.Label = row.Get("Label") ?? $"Remark type {id}";
            remarkType.Description = row.Get("Description");
            remarkType.AssetType = assetType;
            remarkType.AssetTypeTag = row.Get("Asset Type Tag");
            remarkType.SeverityLabel = row.Get("Severity Display");
            remarkType.Resolution = row.Get("Resolution");
            remarkType.OwnerResponsible = row.GetBool("Owner Responsible") ?? false;
            remarkType.Active = row.GetBool("Active") ?? true;
            remarkType.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "remark types");
        if (newTypes.Count > 0)
            ctx.Result.Notes.Add($"Asset types not imported yet {(dryRun ? "will be" : "were")} created by name: {string.Join(", ", newTypes.Distinct().OrderBy(n => n))}.");

        if (dryRun) return;
        await Db.SaveChangesAsync(ct);

        // Link defects imported earlier, by the "Remark Type ID" in their Uptick data
        var idsByExt = await Db.RemarkTypes.Where(r => r.ExternalId != null)
            .ToDictionaryAsync(r => r.ExternalId!, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);
        var defects = await Db.Defects.Where(d => d.RemarkTypeId == null && d.UptickData != null).ToListAsync(ct);
        var linked = 0;
        foreach (var defect in defects)
        {
            if (RemarkTypeIdIn(defect.UptickData) is { } ext && idsByExt.TryGetValue(ext, out var remarkTypeId))
            {
                defect.RemarkTypeId = remarkTypeId;
                linked++;
            }
        }

        if (linked > 0)
        {
            ctx.Result.Notes.Add($"{linked} existing defect{(linked == 1 ? "" : "s")} were linked to their remark type.");
            await Db.SaveChangesAsync(ct);
        }
    }

    private static string? RemarkTypeIdIn(string? uptickData)
    {
        if (string.IsNullOrWhiteSpace(uptickData)) return null;
        using var doc = JsonDocument.Parse(uptickData);
        return doc.RootElement.TryGetProperty("Remark Type ID", out var v) ? v.GetString()?.Trim() : null;
    }
}
