using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Imports;
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

        // Routines this task was generated from (Routines page → Generate tasks); for imported Uptick routine tasks, the
        // property's raised routines its scope of works names ("- 10 - … (AS1851-2012 Section 10): Annual") that are due in its month
        var routines = await _db.RoutineOccurrences.AsNoTracking()
            .Include(o => o.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
            .Where(o => o.ClientTaskId == id)
            .ToListAsync(ct);
        var visits = routines.Count > 0 ? routines : await RoutineCompletion.InScopeAsync(_db, _db.RoutineOccurrences.AsNoTracking()
            .Where(o => o.ClientTaskId == null && (o.Status == RoutineOccurrenceStatus.Generated || o.Status == RoutineOccurrenceStatus.Complete)), task, ct);

        var siteRoutines = routines.Select(o => new RoutineAssets.SiteRoutine(o.SiteId, o.Routine, o.RoutineServiceLevel?.RoutineServiceType.Name ?? RoutineAssets.RoutineType(o.Routine))).ToList();
        // Imported tasks list assets for every routine their scope names, even ones whose routine isn't imported
        if (siteRoutines.Count == 0 && UptickJson.Value(task.UptickData, "Scope of works") is { } scope)
            siteRoutines = RoutineAssets.FromScope(task.SiteId, scope, await _db.RoutineServiceTypes.AsNoTracking().Select(t => t.Name).ToListAsync(ct)).ToList();
        var serviceAssets = await ServiceAssetsAsync(id, siteRoutines, ct);
        var assetIds = serviceAssets.Select(a => a.Asset.Id).Distinct().ToList();

        var vm = new TaskPageVm
        {
            Task = task,
            Tab = TaskPageVm.Tabs.Any(t => t.Key == tab) ? tab! : "task",
            Uptick = TaskPageVm.ParseUptick(task.UptickData),
            Routines = routines.Select(o => o.Routine).Distinct().Order().ToList(),
            ServiceRoutines = siteRoutines.Select(r => r.Routine).ToList(),
            ServiceAssets = serviceAssets,
            Visits = visits.OrderBy(o => o.Routine).ThenBy(o => o.DueDate).ToList(),
            OpenRemarks = await _db.Defects.AsNoTracking()
                .Where(d => d.Active && d.AssetId != null && assetIds.Contains(d.AssetId.Value))
                .GroupBy(d => d.AssetId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, ct),
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
        vm.Remarks = await RemarksAsync(vm, assetIds, ct);
        return vm;
    }

    // The Remark registry: remarks identified on the task (Uptick's "Created on Task Ref"), and ones it resolved: remarks it
    // was raised to repair, and remarks on the assets it services closed off while its work was being done
    private async Task<IReadOnlyList<(Defect Remark, bool Resolved)>> RemarksAsync(TaskPageVm vm, List<int> assetIds, CancellationToken ct)
    {
        var task = vm.Task;
        var since = DateTime.SpecifyKind(vm.WorkFrom.AddDays(-1), DateTimeKind.Utc);
        var remarks = await _db.Defects.AsNoTracking()
            .Include(d => d.Asset)
            .Where(d => d.SiteId == task.SiteId
                        && ((task.Ref != null && d.RaisedOnTaskRef == task.Ref)
                            || d.RepairTaskId == task.Id
                            || (!d.Active && d.AssetId != null && assetIds.Contains(d.AssetId.Value) && d.InactiveUtc >= since)))
            .ToListAsync(ct);

        bool ClosedDuringWork(Defect d) => d.InactiveUtc?.ToSydney().Date is DateTime closed
            && closed >= vm.WorkFrom && (vm.WorkTo is not DateTime to || closed <= to);

        return remarks
            .Select(d => (d, Resolved: !d.Active && (d.RepairTaskId == task.Id || ClosedDuringWork(d))))
            .Where(r => r.Resolved || r.d.RaisedOnTaskRef == task.Ref && task.Ref != null)
            .OrderBy(r => r.d.Asset?.InspectionOrder == null).ThenBy(r => r.d.Asset?.InspectionOrder).ThenBy(r => r.d.Asset?.Name)
            .ThenBy(r => r.Resolved).ThenBy(r => r.d.Id)
            .ToList();
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
