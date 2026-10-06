using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.MaintenanceSchedules;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.MaintenanceSchedules;

public class MaintenanceScheduleService : IMaintenanceScheduleService
{
    private readonly AppDbContext _db;

    public MaintenanceScheduleService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MaintenanceScheduleListItemVm>> GetDueListAsync(MaintenanceScheduleFilter filter)
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        var q = _db.MaintenanceSchedules
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Where(s =>
                (s.TargetType == ScheduleTargetType.Site && s.Site!.Active && s.Site.Client.Active) ||
                (s.TargetType == ScheduleTargetType.Asset && s.Asset!.Site.Active && s.Asset.Site.Client.Active));

        if (filter.TargetType.HasValue)
            q = q.Where(s => s.TargetType == filter.TargetType.Value);

        q = filter.Window switch
        {
            ScheduleWindow.Month => q.Where(s => s.NextRunDate >= monthStart && s.NextRunDate < monthEnd),
            ScheduleWindow.Overdue => q.Where(s => s.NextRunDate < today),
            _ => q
        };

        q = filter.GenerationStatus switch
        {
            ScheduleGenerationStatus.Pending => q.Where(s => !_db.ClientTasks.Any(t =>
                t.MaintenanceScheduleId == s.Id &&
                t.DueDateUtc == s.NextRunDate &&
                t.Status != ClientTaskStatus.Cancelled)),
            ScheduleGenerationStatus.Generated => q.Where(s => _db.ClientTasks.Any(t =>
                t.MaintenanceScheduleId == s.Id &&
                t.DueDateUtc == s.NextRunDate &&
                t.Status != ClientTaskStatus.Cancelled)),
            _ => q
        };

        var rows = await q
            .OrderBy(s => s.NextRunDate)
            .Select(s => new
            {
                s.Id,
                s.FyreRef,
                s.TargetType,
                s.NextRunDate,
                IntervalName = s.MaintenanceInterval.Name,
                ClientName = s.TargetType == ScheduleTargetType.Site ? s.Site!.Client.Name : s.Asset!.Site.Client.Name,
                TargetLabel = s.TargetType == ScheduleTargetType.Asset
                    ? s.Asset!.Site.Name + " • " + s.Asset.Name
                    : s.Site!.Name,
                GeneratedTask = _db.ClientTasks
                    .Where(t => t.MaintenanceScheduleId == s.Id &&
                                t.DueDateUtc == s.NextRunDate &&
                                t.Status != ClientTaskStatus.Cancelled)
                    .Select(t => new { t.Id, t.Status })
                    .FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(r => new MaintenanceScheduleListItemVm
        {
            Id = r.Id,
            DisplayRef = r.FyreRef,
            ClientName = r.ClientName,
            TargetLabel = r.TargetLabel,
            IntervalName = r.IntervalName,
            NextRunDate = r.NextRunDate,
            IsOverdue = r.NextRunDate.Date < today,
            GeneratedTaskId = r.GeneratedTask?.Id,
            GeneratedTaskStatus = r.GeneratedTask?.Status
        }).ToList();
    }

    public async Task<MaintenanceSchedule?> GetDetailsAsync(int id)
    {
        return await _db.MaintenanceSchedules
            .Include(s => s.MaintenanceInterval)
            .Include(s => s.Site).ThenInclude(s => s!.Client)
            .Include(s => s.Asset).ThenInclude(a => a!.Site).ThenInclude(s => s.Client)
            .Include(s => s.MaintenanceHistory)
            .Include(s => s.GeneratedTasks)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<ClientTask> GenerateTaskAsync(int scheduleId, string createdByUserId)
    {
        var schedule = await _db.MaintenanceSchedules
            .Include(s => s.MaintenanceInterval)
            .Include(s => s.Site).ThenInclude(s => s!.Client)
            .Include(s => s.Asset).ThenInclude(a => a!.Site).ThenInclude(s => s.Client)
            .FirstOrDefaultAsync(s => s.Id == scheduleId);

        if (schedule == null)
            throw new InvalidOperationException($"Maintenance schedule {scheduleId} not found.");

        var existing = await _db.ClientTasks.FirstOrDefaultAsync(t =>
            t.MaintenanceScheduleId == scheduleId &&
            t.DueDateUtc == schedule.NextRunDate &&
            t.Status != ClientTaskStatus.Cancelled);

        if (existing != null)
            return existing;

        int clientId;
        int siteId;
        string title;

        if (schedule.TargetType == ScheduleTargetType.Asset && schedule.Asset != null)
        {
            clientId = schedule.Asset.Site.ClientId;
            siteId = schedule.Asset.SiteId;
            title = $"{schedule.MaintenanceInterval.Name} maintenance — {schedule.Asset.Site.Name} • {schedule.Asset.Name}";
        }
        else if (schedule.Site != null)
        {
            clientId = schedule.Site.ClientId;
            siteId = schedule.Site.Id;
            title = $"{schedule.MaintenanceInterval.Name} maintenance — {schedule.Site.Name}";
        }
        else
        {
            throw new InvalidOperationException($"Maintenance schedule {scheduleId} has no valid target.");
        }

        var task = new ClientTask
        {
            ClientId = clientId,
            SiteId = siteId,
            Title = title,
            Status = ClientTaskStatus.Open,
            Priority = ClientTaskPriority.Normal,
            DueDateUtc = schedule.NextRunDate,
            CreatedUtc = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
            MaintenanceScheduleId = schedule.Id
        };

        _db.ClientTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    public async Task<ScheduleCompleteStatus> CompleteAsync(int scheduleId, string? notes)
    {
        var schedule = await _db.MaintenanceSchedules
            .Include(s => s.MaintenanceInterval)
            .FirstOrDefaultAsync(s => s.Id == scheduleId);

        if (schedule == null) return ScheduleCompleteStatus.NotFound;
        if (!schedule.IsActive) return ScheduleCompleteStatus.Inactive;

        var months = schedule.MaintenanceInterval?.Months ?? 0;
        if (months <= 0) return ScheduleCompleteStatus.InvalidInterval;

        _db.MaintenanceHistory.Add(new MaintenanceHistory
        {
            MaintenanceScheduleId = schedule.Id,
            CompletedAt = DateTime.UtcNow,
            DueDateAtCompletion = schedule.NextRunDate,
            Notes = notes
        });

        // Advance from due date (prevents drift)
        schedule.NextRunDate = schedule.NextRunDate.Date.AddMonths(months);

        await _db.SaveChangesAsync();
        return ScheduleCompleteStatus.Success;
    }
}
