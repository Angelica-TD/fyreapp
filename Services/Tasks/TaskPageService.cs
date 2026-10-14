using FyreApp.Data;
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

        return new TaskPageVm
        {
            Task = task,
            Tab = TaskPageVm.Tabs.Any(t => t.Key == tab) ? tab! : "task",
            Uptick = TaskPageVm.ParseUptick(task.UptickData),
            // Routines this task was generated from (Routines page → Generate tasks)
            Routines = await _db.RoutineOccurrences.AsNoTracking()
                .Where(o => o.ClientTaskId == id)
                .OrderBy(o => o.Routine)
                .Select(o => o.Routine)
                .ToListAsync(ct),
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
            Techs = (await _db.UptickUsers.AsNoTracking()
                    .Assignable()
                    .OrderBy(u => u.Name)
                    .Select(u => new { u.Id, u.Name, u.LicenseType })
                    .ToListAsync(ct))
                .Select(u => (u.Id.ToString(), u.LicenseType == "CONTRACTOR" ? u.Name + " (contractor)" : u.Name))
                .ToList()
        };
    }
}
