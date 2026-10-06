using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Routine service level types" export -> RoutineServiceLevel (reference data), under its routine
// service type. The routines import uses these to get each "<routine>: <level>" occurrence's interval.
public class RoutineServiceLevelImporter : UptickImporter
{
    public RoutineServiceLevelImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.RoutineServiceLevels;
    public override string DisplayName => "Routine service levels";
    public override string[] SignatureHeaders => new[] { "Display Code", "Routine Service Type ID", "Interval" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var types = await Db.RoutineServiceTypes.ToListAsync(ct);
        var typesByExt = types.Where(t => t.ExternalId != null).ToDictionary(t => t.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var typesByName = types.GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var levels = await Db.RoutineServiceLevels.Include(l => l.RoutineServiceType).ToListAsync(ct);
        var byExt = levels.Where(l => l.ExternalId != null).ToDictionary(l => l.ExternalId!, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;
        var newTypes = new List<string>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, new HashSet<string>(), seen, row, ctx)) continue;

            var typeName = row.Get("Routine Service Type Name") ?? "(no routine)";
            var type = typesByExt.GetValueOrDefault(row.Get("Routine Service Type ID") ?? "") ?? typesByName.GetValueOrDefault(typeName);
            if (type == null)
            {
                type = new RoutineServiceType { Name = typeName, ExternalId = row.Get("Routine Service Type ID") };
                typesByName[typeName] = type;
                if (type.ExternalId != null) typesByExt[type.ExternalId] = type;
                newTypes.Add(typeName);
                if (!dryRun) Db.RoutineServiceTypes.Add(type);
            }

            var months = IntervalMonths(row.GetInt("Interval"), row.Get("Frequency"));
            if (months == null)
                ctx.Issue("Unknown interval", id!, row.RowNumber,
                    $"Interval '{row.Get("Interval")} {row.Get("Frequency")}' isn't in months or years; imported without an interval.");

            if (!byExt.TryGetValue(id!, out var level))
            {
                level = new RoutineServiceLevel();
                byExt[id!] = level;
                if (!dryRun) Db.RoutineServiceLevels.Add(level);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            level.ExternalId = id;
            level.RoutineServiceType = type;
            level.Name = row.Get("Name") ?? $"Level {id}";
            level.DisplayCode = row.Get("Display Code");
            level.IntervalMonths = months ?? 0;
            level.OffsetMonths = row.GetInt("Offset");
            level.ToleranceInterval = row.GetInt("Tolerance Interval");
            level.ToleranceUnit = row.Get("Tolerance Unit");
            level.DefaultEnabled = row.GetBool("Default Enabled?") ?? false;
            level.AssetBased = row.GetBool("Asset Based?") ?? false;
            level.SupersedesLowerRank = row.GetBool("Supersedes Lower Rank?") ?? false;
            level.ServiceRank = row.GetInt("Service Rank");
            level.Custom = row.GetBool("Custom?") ?? false;
            level.Active = row.GetBool("Active?") ?? true;
            level.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "routine service levels");
        if (newTypes.Count > 0)
            ctx.Result.Notes.Add($"Routine service types not imported yet {(dryRun ? "will be" : "were")} created by name: {string.Join(", ", newTypes.Distinct().OrderBy(n => n))}.");
        if (await Db.MaintenanceSchedules.AnyAsync(ct))
            ctx.Result.Notes.Add("Re-run the routine schedules import to link existing schedules to these routines.");

        if (!dryRun) await Db.SaveChangesAsync(ct);
    }

    // Uptick "Interval" + "Frequency" unit; every level seen so far is in months ("M")
    public static int? IntervalMonths(int? interval, string? unit) => (interval, unit?.Trim().ToUpperInvariant()) switch
    {
        (> 0, "M" or null or "") => interval,
        (> 0, "Y") => interval * 12,
        _ => null
    };
}
