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
            .Include(t => t.Property)
            .Include(t => t.AssignedTo)
            .Include(t => t.CoveredSchedules).ThenInclude(s => s.MaintenanceInterval)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task == null) return null;

        // Routines this task was generated from (Routines page → Generate tasks)
        var routines = await _db.Routines.AsNoTracking()
            .Include(o => o.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
            .Where(o => o.ClientTaskId == id)
            .ToListAsync(ct);

        var siteRoutines = routines.Select(o => new RoutineAssets.SiteRoutine(o.PropertyId, o.Name, o.RoutineServiceLevel?.RoutineServiceType.Name ?? RoutineAssets.RoutineType(o.Name))).ToList();
        // Imported tasks list assets for every routine their scope names, even ones whose routine isn't imported
        if (siteRoutines.Count == 0 && task.ScopeOfWorks is { } scope)
            siteRoutines = RoutineAssets.FromScope(task.PropertyId, scope, await _db.RoutineServiceTypes.AsNoTracking().Select(t => t.Name).ToListAsync(ct)).ToList();
        var visits = routines.Count > 0
            ? routines.OrderBy(o => o.Name).ThenBy(o => o.DueDate).Select(o => (o.Name, (Routine?)o)).ToList()
            : await NearestRoutinesAsync(task, siteRoutines.Select(r => r.Routine).ToList(), ct);
        var serviceAssets = await ServiceAssetsAsync(id, siteRoutines, ct);
        var assetIds = serviceAssets.Select(a => a.Asset.Id).Distinct().ToList();

        var vm = new TaskPageVm
        {
            Task = task,
            Tab = TaskPageVm.Tabs.Any(t => t.Key == tab) ? tab! : "task",
            Uptick = await TaskUptickFields.ForAsync(_db, task, ct),
            Routines = routines.Select(o => o.Name).Distinct().Order().ToList(),
            ServiceRoutines = siteRoutines.Select(r => r.Routine).ToList(),
            ServiceAssets = serviceAssets,
            Visits = visits,
            OpenRemarks = await _db.Remarks.AsNoTracking()
                .Where(d => d.Active && d.AssetId != null && assetIds.Contains(d.AssetId.Value))
                .GroupBy(d => d.AssetId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, ct),
            // Reports done on this task, and remarks it was raised to repair (linked from Uptick imports)
            Reports = await _db.Reports.AsNoTracking()
                .Where(r => r.ClientTaskId == id)
                .OrderByDescending(r => r.InspectedDate)
                .ToListAsync(ct),
            Repairs = await _db.Remarks.AsNoTracking()
                .Include(d => d.Asset)
                .Where(d => d.RepairTaskId == id)
                .OrderBy(d => d.Asset != null ? d.Asset.Name : "")
                .ThenBy(d => d.Id)
                .ToListAsync(ct),
            Invoices = await _db.Invoices.AsNoTracking()
                .Where(i => i.ClientTaskId == id)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct),
            // Uptick form responses name the task by its Uptick ID
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
        vm.PerformedOn = await PerformedAsync(task, serviceAssets, ct);
        vm.Remarks = await RemarksAsync(vm, assetIds, ct);
        return vm;
    }

    // An imported Uptick routine task's routines, one per routine its scope of works names: the property's raised or
    // completed routine of that name due in the task's month, else nearest it (within a couple of months either side)
    private async Task<List<(string Routine, Routine? Occurrence)>> NearestRoutinesAsync(
        ClientTask task, IReadOnlyList<string> names, CancellationToken ct)
    {
        if (names.Count == 0 || task.DueDateUtc is not DateTime due) return names.Order().Select(n => (n, (Routine?)null)).ToList();

        var from = due.AddMonths(-2);
        var to = due.AddMonths(2);
        var found = (await _db.Routines.AsNoTracking()
                .Where(o => o.PropertyId == task.PropertyId && (o.ClientTaskId == null || o.ClientTaskId == task.Id)
                            && (o.Status == RoutineStatus.Generated || o.Status == RoutineStatus.Complete)
                            && o.DueDate >= from && o.DueDate <= to)
                .ToListAsync(ct))
            .Where(o => names.Contains(o.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return names.Order()
            .Select(n => (n, found.Where(o => string.Equals(o.Name, n, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.DueDate.Year == due.Year && o.DueDate.Month == due.Month ? 0 : 1)
                .ThenBy(o => Math.Abs((o.DueDate - due).Ticks))
                .FirstOrDefault()))
            .ToList();
    }

    // Which of its assets the task serviced. Uptick's exports give only each asset's last service date, so that goes to
    // the property's task whose scope of works names the asset's routine and that was being worked on then: the one
    // performed that day, else the one started most recently by then. So a later task servicing the asset doesn't
    // count for an earlier one still open.
    private async Task<IReadOnlyDictionary<int, DateTime>> PerformedAsync(
        ClientTask task, IReadOnlyList<(string Routine, Asset Asset)> serviceAssets, CancellationToken ct)
    {
        static bool During((DateTime From, DateTime? To) w, DateTime d) => d >= w.From && (w.To is not DateTime to || d <= to);

        var window = TaskPageVm.WorkWindow(task);
        var serviced = serviceAssets
            .Where(a => a.Asset.LastServiceDate is DateTime d && During(window, d.Date))
            .Select(a => (Type: RoutineAssets.RoutineType(a.Routine), a.Asset.Id, Date: a.Asset.LastServiceDate!.Value.Date))
            .ToList();
        if (serviced.Count == 0) return new Dictionary<int, DateTime>();

        // The property's other tasks around then (ones due well before can't win: they started earlier), and the
        // routines their scopes name
        var dueFrom = DateTime.SpecifyKind(window.From.AddMonths(-3), DateTimeKind.Utc);
        var dueTo = DateTime.SpecifyKind(serviced.Max(s => s.Date).AddMonths(3), DateTimeKind.Utc);
        var types = await _db.RoutineServiceTypes.AsNoTracking().Select(t => t.Name).ToListAsync(ct);
        var others = (await _db.ClientTasks.AsNoTracking()
                .Where(t => t.PropertyId == task.PropertyId && t.Id != task.Id && t.Status != ClientTaskStatus.Cancelled
                            && (t.DueDateUtc == null || (t.DueDateUtc >= dueFrom && t.DueDateUtc <= dueTo)))
                .ToListAsync(ct))
            .Select(t => (Window: TaskPageVm.WorkWindow(t),
                          Types: RoutineAssets.FromScope(t.PropertyId, TaskPageVm.Scope(t) ?? "", types).Select(r => r.RoutineType).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .Where(t => t.Types.Count > 0)
            .ToList();

        var performed = new Dictionary<int, DateTime>();
        foreach (var (type, assetId, date) in serviced)
        {
            var rivals = others.Where(o => o.Types.Contains(type) && During(o.Window, date)).Select(o => o.Window).ToList();
            var ours = window.To == date
                       || (!rivals.Any(r => r.To == date) && !rivals.Any(r => r.From > window.From));
            if (ours) performed.TryAdd(assetId, date);
        }
        return performed;
    }

    // The Remark registry: remarks identified on the task (Uptick's "Created on Task Ref"), and ones it resolved: remarks it
    // was raised to repair, and remarks on the assets it serviced closed off while its work was being done
    private async Task<IReadOnlyList<(Remark Remark, bool Resolved)>> RemarksAsync(TaskPageVm vm, List<int> assetIds, CancellationToken ct)
    {
        var task = vm.Task;
        var since = DateTime.SpecifyKind(vm.WorkFrom.AddDays(-1), DateTimeKind.Utc);
        var remarks = await _db.Remarks.AsNoTracking()
            .Include(d => d.Asset)
            .Where(d => d.PropertyId == task.PropertyId
                        && ((task.Ref != null && d.RaisedOnTaskRef == task.Ref)
                            || d.RepairTaskId == task.Id
                            || (!d.Active && d.AssetId != null && assetIds.Contains(d.AssetId.Value) && d.InactiveUtc >= since)))
            .ToListAsync(ct);

        bool ClosedDuringWork(Remark d) => d.AssetId is int assetId && vm.PerformedOn.ContainsKey(assetId) && d.InactiveUtc?.ToSydney().Date is DateTime closed
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
        var saved = await _db.ServiceTasks.AsNoTracking()
            // (lines of stock used have no asset)
            .Where(a => a.ClientTaskId == taskId && a.AssetId != null)
            .Select(a => new { a.Id, a.Routine, Asset = a.Asset! })
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
