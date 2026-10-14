using FyreApp.Data;
using FyreApp.Services.Imports;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

// Which assets a routine services. Uptick's asset types export lists each type's "Applicable Routines"
// (e.g. Fire Extinguisher → "10 - Portable and Wheeled Fire Extinguishers", one per line) and its
// "Excluded Routine Levels"; a property's active assets of those types are the ones its routine covers.
public static class RoutineAssets
{
    public record AssetLine(int AssetId, string Routine);

    // A routine at a site: its full name ("<type>: <level>") and its routine service type's name
    public record SiteRoutine(int SiteId, string Routine, string RoutineType);

    // The asset lines for each routine, in inspection order: one per asset per routine
    public static async Task<ILookup<(int SiteId, string Routine), AssetLine>> ForAsync(
        AppDbContext db, IReadOnlyCollection<SiteRoutine> routines, CancellationToken ct = default)
    {
        var siteIds = routines.Select(r => r.SiteId).Distinct().ToList();
        if (siteIds.Count == 0) return Array.Empty<(int, string, AssetLine)>().ToLookup(x => (x.Item1, x.Item2), x => x.Item3);

        var assets = await db.Assets.AsNoTracking()
            .Where(a => siteIds.Contains(a.SiteId) && a.IsActive)
            .OrderBy(a => a.InspectionOrder == null).ThenBy(a => a.InspectionOrder).ThenBy(a => a.Ref).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.SiteId, Types = a.AssetTypes.Select(t => new { t.Id, t.UptickData }) })
            .ToListAsync(ct);

        // Each asset type's routines, parsed once
        var typeRoutines = assets.SelectMany(a => a.Types).DistinctBy(t => t.Id)
            .ToDictionary(t => t.Id, t => (Applicable: Lines(UptickJson.Value(t.UptickData, "Applicable Routines")),
                                           Excluded: Lines(UptickJson.Value(t.UptickData, "Excluded Routine Levels"))));

        var lines = new List<(int SiteId, string Routine, AssetLine Line)>();
        foreach (var r in routines.DistinctBy(r => (r.SiteId, r.Routine)))
        {
            foreach (var a in assets.Where(a => a.SiteId == r.SiteId))
            {
                if (a.Types.Any(t => Applies(typeRoutines[t.Id].Applicable, typeRoutines[t.Id].Excluded, r)))
                    lines.Add((r.SiteId, r.Routine, new AssetLine(a.Id, r.Routine)));
            }
        }
        return lines.ToLookup(l => (l.SiteId, l.Routine), l => l.Line);
    }

    // The type's routines include this one, and its level isn't excluded (named as the routines export
    // names it, "<type>: <level>")
    public static bool Applies(IReadOnlyCollection<string> applicable, IReadOnlyCollection<string> excluded, SiteRoutine routine) =>
        applicable.Contains(routine.RoutineType, StringComparer.OrdinalIgnoreCase)
        && !excluded.Contains(routine.Routine, StringComparer.OrdinalIgnoreCase);

    // Uptick puts one routine per line
    public static IReadOnlyList<string> Lines(string? value) =>
        (value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // The routines an imported Uptick task's "Scope of works" names, one per line:
    // "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual"
    // → "10 - Portable and Wheeled Fire Extinguishers: Annual". Lines not starting with a known routine are skipped.
    public static IReadOnlyList<SiteRoutine> FromScope(int siteId, string scope, IEnumerable<string> routineTypes)
    {
        // Longest first, so "06 - Fire Detection (Fire Panels)" wins over a shorter name it starts with
        var types = routineTypes.OrderByDescending(t => t.Length).ToList();
        var routines = new List<SiteRoutine>();
        foreach (var line in Lines(scope))
        {
            var text = line.TrimStart('-', ' ');
            var type = types.FirstOrDefault(t => text.StartsWith(t, StringComparison.OrdinalIgnoreCase));
            if (type == null) continue;

            var i = text.LastIndexOf(": ", StringComparison.Ordinal);
            var level = i < type.Length ? null : text[(i + 2)..].Trim();
            routines.Add(new SiteRoutine(siteId, string.IsNullOrEmpty(level) ? type : $"{type}: {level}", type));
        }
        return routines.DistinctBy(r => r.Routine).ToList();
    }

    // "10 - Portable and Wheeled Fire Extinguishers: Six-monthly" → "10 - Portable and Wheeled Fire Extinguishers"
    public static string RoutineType(string routine)
    {
        var i = routine.LastIndexOf(": ", StringComparison.Ordinal);
        return i < 0 ? routine : routine[..i];
    }
}
