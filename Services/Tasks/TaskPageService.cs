using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.ViewModels.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Tasks;

public interface ITaskPageService
{
    Task<TaskPageVm?> LoadAsync(int id, string? tab, CancellationToken ct = default);
}

// The task page (/Task/Details/{id})
public class TaskPageService : ITaskPageService
{
    private readonly AppDbContext _db;
    public TaskPageService(AppDbContext db) => _db = db;

    public async Task<TaskPageVm?> LoadAsync(int id, string? tab, CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking()
            .Include(t => t.Client)
            .Include(t => t.Site)
            .Include(t => t.AssignedTo)
            .Include(t => t.CoveredSchedules).ThenInclude(s => s.MaintenanceInterval)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task == null) return null;

        // Routines this task was generated from (Routines page → Generate tasks)
        var routines = await _db.RoutineOccurrences.AsNoTracking()
            .Where(o => o.ClientTaskId == id)
            .OrderBy(o => o.Routine)
            .Select(o => new { o.SiteId, o.Routine, Type = o.RoutineServiceLevel != null ? o.RoutineServiceLevel.RoutineServiceType.Name : null })
            .ToListAsync(ct);

        return new TaskPageVm
        {
            Task = task,
            Tab = TaskPageVm.Tabs.Any(t => t.Key == tab) ? tab! : "task",
            Uptick = TaskPageVm.ParseUptick(task.UptickData),
            Routines = routines.Select(o => o.Routine).Distinct().ToList(),
            ServiceAssets = await ServiceAssetsAsync(id, routines.Select(o => new RoutineAssets.SiteRoutine(o.SiteId, o.Routine, o.Type ?? RoutineAssets.RoutineType(o.Routine))).ToList(), ct),
            // Reports done on this task, and remarks it was raised to repair (linked from Uptick imports)
            Reports = await _db.ServiceReports.AsNoTracking()
                .Where(r => r.ClientTaskId == id)
                .OrderByDescending(r => r.InspectedDate)
                .ToListAsync(ct),
            Repairs = await _db.Defects.AsNoTracking()
                .Include(d => d.Asset)
                .Where(d => d.RepairTaskId == id)
                .OrderBy(d => d.Asset != null ? d.Asset.Name : "")
                .ThenBy(d => d.Id)
                .ToListAsync(ct),
            // Uptick invoices and form responses name the task by its Uptick ID
            Invoices = task.ExternalId == null ? [] : await _db.Invoices.AsNoTracking()
                .Where(i => i.TaskExternalId == task.ExternalId)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct),
            Forms = task.ExternalId == null ? [] : (await (
                    from f in _db.FormResponses.AsNoTracking()
                    where f.TargetContentType == FormResponse.TaskContentType && f.TargetObjectId == task.ExternalId
                    join u in _db.UptickUsers on f.SubmitterExternalId equals u.ExternalId into submitters
                    from u in submitters.DefaultIfEmpty()
                    orderby f.SubmittedUtc
                    select new { f, Name = u != null ? u.Name : null })
                .ToListAsync(ct))
                .Select(x => (x.f, x.Name))
                .ToList(),
            Techs = (await _db.UptickUsers.AsNoTracking()
                    .Assignable()
                    .OrderBy(u => u.Name)
                    .Select(u => new { u.Id, u.Name, u.LicenseType })
                    .ToListAsync(ct))
                .Select(u => (u.Id.ToString(), u.LicenseType == "CONTRACTOR" ? u.Name + " (contractor)" : u.Name))
                .ToList()
        };
    }

    // The assets the task services for its routines: saved when it was generated; for routine tasks generated
    // before they were saved, worked out from the property's assets now
    private async Task<IReadOnlyList<(string Routine, Asset Asset)>> ServiceAssetsAsync(
        int taskId, IReadOnlyCollection<RoutineAssets.SiteRoutine> routines, CancellationToken ct)
    {
        var saved = await _db.TaskAssets.AsNoTracking()
            .Where(a => a.ClientTaskId == taskId)
            .Select(a => new { a.Id, a.Routine, a.Asset })
            .ToListAsync(ct);
        if (saved.Count > 0 || routines.Count == 0)
            return Ordered(saved.Select(a => (a.Routine, a.Asset)));

        var lines = await RoutineAssets.ForAsync(_db, routines, ct);
        var assetIds = lines.SelectMany(g => g).Select(l => l.AssetId).Distinct().ToList();
        var assets = await _db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return Ordered(lines.SelectMany(g => g).Select(l => (l.Routine, assets[l.AssetId])));
    }

    // By routine, then in inspection order
    private static IReadOnlyList<(string Routine, Asset Asset)> Ordered(IEnumerable<(string Routine, Asset Asset)> lines) =>
        lines.OrderBy(l => l.Routine)
            .ThenBy(l => l.Asset.InspectionOrder == null).ThenBy(l => l.Asset.InspectionOrder)
            .ThenBy(l => l.Asset.Ref).ThenBy(l => l.Asset.Name).ThenBy(l => l.Asset.Id)
            .ToList();
}
