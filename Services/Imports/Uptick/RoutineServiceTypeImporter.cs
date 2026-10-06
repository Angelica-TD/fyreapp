using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Routine service types" export -> RoutineServiceType (reference data). Matched on Uptick ID, then name.
public class RoutineServiceTypeImporter : UptickImporter
{
    public RoutineServiceTypeImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.RoutineServiceTypes;
    public override string DisplayName => "Routine service types";
    public override string[] SignatureHeaders => new[] { "Name", "Standard Reference", "Default Performance Standard" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var existing = await Db.RoutineServiceTypes.ToListAsync(ct);
        var byExt = existing.Where(t => t.ExternalId != null).ToDictionary(t => t.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var byName = existing.GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, new HashSet<string>(), seen, row, ctx)) continue;

            var name = row.Get("Name") ?? $"Routine {id}";
            var type = byExt.GetValueOrDefault(id!) ?? byName.GetValueOrDefault(name);
            if (type == null)
            {
                type = new RoutineServiceType();
                byName[name] = type;
                if (!dryRun) Db.RoutineServiceTypes.Add(type);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            type.ExternalId = id;
            type.Name = name;
            type.Standard = row.Get("Standard");
            type.StandardReference = row.Get("Standard Reference");
            type.StandardNotes = row.Get("Standard Notes");
            type.DefaultPerformanceStandard = row.Get("Default Performance Standard");
            type.ServiceGroup = row.Get("Service Group");
            type.Custom = row.GetBool("Custom") ?? false;
            type.Active = row.GetBool("Active") ?? true;
            type.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "routine service types");

        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
