using FyreApp.Data;
using FyreApp.Services.Imports;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

// Which assets a routine services. Uptick gives each asset type its applicable routines (e.g. Fire Extinguisher →
// "10 - Portable and Wheeled Fire Extinguishers") and excluded routine levels; a property's active assets of those
// types are the ones its routine covers.
public static class RoutineAssets
{
    public record AssetLine(int AssetId, string Routine);

    // A routine at a site: its full name ("<type>: <level>") and its routine service type's name
    public record SiteRoutine(int PropertyId, string Routine, string RoutineType);

    // The asset lines for each routine, in inspection order: one per asset per routine
    public static async Task<ILookup<(int PropertyId, string Routine), AssetLine>> ForAsync(
        AppDbContext db, IReadOnlyCollection<SiteRoutine> routines, CancellationToken ct = default)
    {
        var siteIds = routines.Select(r => r.PropertyId).Distinct().ToList();
        if (siteIds.Count == 0) return Array.Empty<(int, string, AssetLine)>().ToLookup(x => (x.Item1, x.Item2), x => x.Item3);

        var assets = await db.Assets.AsNoTracking()
            .Where(a => siteIds.Contains(a.PropertyId) && a.IsActive)
            .OrderBy(a => a.InspectionOrder == null).ThenBy(a => a.InspectionOrder).ThenBy(a => a.Ref).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.PropertyId, Types = a.AssetTypes.Select(t => t.Id).ToList() })
            .ToListAsync(ct);

        var typeRoutines = await TypeRoutinesAsync(db, assets.SelectMany(a => a.Types), ct);

        var lines = new List<(int PropertyId, string Routine, AssetLine Line)>();
        foreach (var r in routines.DistinctBy(r => (r.PropertyId, r.Routine)))
        {
            foreach (var a in assets.Where(a => a.PropertyId == r.PropertyId))
            {
                if (a.Types.Any(t => Applies(typeRoutines[t].Applicable, typeRoutines[t].Excluded, r)))
                    lines.Add((r.PropertyId, r.Routine, new AssetLine(a.Id, r.Routine)));
            }
        }
        return lines.ToLookup(l => (l.PropertyId, l.Routine), l => l.Line);
    }

    // Each asset type's applicable routines (by name) and excluded levels ("<routine>: <level>"), from Uptick's links
    public static async Task<Dictionary<int, (IReadOnlyList<string> Applicable, IReadOnlyList<string> Excluded)>> TypeRoutinesAsync(
        AppDbContext db, IEnumerable<int> typeIds, CancellationToken ct = default)
    {
        var ids = typeIds.Distinct().ToList();
        var types = await db.AssetTypes.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                Applicable = t.ApplicableRoutineTypes.Select(r => r.Name).ToList(),
                Excluded = t.ExcludedRoutineLevels.Select(l => l.RoutineServiceType.Name + ": " + l.Name).ToList()
            })
            .ToListAsync(ct);
        return types.ToDictionary(t => t.Id, t => ((IReadOnlyList<string>)t.Applicable, (IReadOnlyList<string>)t.Excluded));
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
