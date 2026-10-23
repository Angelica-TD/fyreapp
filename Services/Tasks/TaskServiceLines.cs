using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Routines;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Tasks;

// The assets a task services, each for one of its routines: the task page's Work tab and the field app's list
public static class TaskServiceLines
{
    // The task's routines at its property: those it was generated from, else (imported tasks) every routine its
    // scope of works names, even ones whose routine isn't imported
    public static async Task<IReadOnlyList<RoutineAssets.SiteRoutine>> SiteRoutinesAsync(
        AppDbContext db, ClientTask task, IEnumerable<Routine> routines, CancellationToken ct)
    {
        var siteRoutines = routines.Select(o => new RoutineAssets.SiteRoutine(o.PropertyId, o.Name, o.RoutineServiceLevel?.RoutineServiceType.Name ?? RoutineAssets.RoutineType(o.Name))).ToList();
        if (siteRoutines.Count == 0 && task.ScopeOfWorks is { } scope)
            siteRoutines = RoutineAssets.FromScope(task.PropertyId, scope, await db.RoutineServiceTypes.AsNoTracking().Select(t => t.Name).ToListAsync(ct)).ToList();
        return siteRoutines;
    }

    // Loads the task's routines first
    public static async Task<IReadOnlyList<(string Routine, Asset Asset)>> ForTaskAsync(AppDbContext db, ClientTask task, CancellationToken ct)
    {
        var routines = await db.Routines.AsNoTracking()
            .Include(o => o.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
            .Where(o => o.ClientTaskId == task.Id)
            .ToListAsync(ct);
        return await AssetsAsync(db, task.Id, await SiteRoutinesAsync(db, task, routines, ct), ct);
    }

    // The assets the task services for its routines: saved (ServiceTask) when it was generated, migrated or worked
    // on in the field; otherwise worked out from the property's assets now
    public static async Task<IReadOnlyList<(string Routine, Asset Asset)>> AssetsAsync(
        AppDbContext db, int taskId, IReadOnlyCollection<RoutineAssets.SiteRoutine> routines, CancellationToken ct)
    {
        var saved = await db.ServiceTasks.AsNoTracking()
            // (lines of stock used have no asset)
            .Where(a => a.ClientTaskId == taskId && a.AssetId != null)
            .Select(a => new { a.Id, a.Routine, Asset = a.Asset! })
            .ToListAsync(ct);
        if (saved.Count > 0 || routines.Count == 0)
            return Ordered(saved.Select(a => (a.Routine, a.Asset)));

        var lines = await RoutineAssets.ForAsync(db, routines, ct);
        var assetIds = lines.SelectMany(g => g).Select(l => l.AssetId).Distinct().ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return Ordered(lines.SelectMany(g => g).Select(l => (l.Routine, assets[l.AssetId])));
    }

    // By routine, then in inspection order
    private static IReadOnlyList<(string Routine, Asset Asset)> Ordered(IEnumerable<(string Routine, Asset Asset)> lines) =>
        lines.OrderBy(l => l.Routine)
            .ThenBy(l => l.Asset.InspectionOrder == null).ThenBy(l => l.Asset.InspectionOrder)
            .ThenBy(l => l.Asset.Ref).ThenBy(l => l.Asset.Name).ThenBy(l => l.Asset.Id)
            .ToList();
}
