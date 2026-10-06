using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.ViewModels.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace FyreApp.Services;

public class ClientTaskService : IClientTaskService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaintenanceScheduleService _scheduleService;

    private static readonly TimeZoneInfo SydneyTz =
        TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public ClientTaskService(AppDbContext db, UserManager<ApplicationUser> userManager, IMaintenanceScheduleService scheduleService)
    {
        _db = db;
        _userManager = userManager;
        _scheduleService = scheduleService;
    }

    public async Task<IReadOnlyList<ClientTaskListItemVm>> GetAllAsync(
        string? statusFilter = null,
        string? clientFilter = null)
    {
        var q = _db.ClientTasks
            .Include(t => t.Client)
            .Include(t => t.Site)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<ClientTaskStatus>(statusFilter, out var status))
        {
            q = q.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(clientFilter) &&
            int.TryParse(clientFilter, out var clientId))
        {
            q = q.Where(t => t.ClientId == clientId);
        }

        return await q
            .OrderByDescending(t => t.CreatedUtc)
            .Select(t => new ClientTaskListItemVm
            {
                Id = t.Id,
                Title = t.Title,
                ClientName = t.Client.Name,
                AddressDisplay = t.Site.AddressDisplay ?? t.Site.Name,
                Priority = t.Priority,
                Status = t.Status,
                DueDate = t.DueDateUtc,
                CreatedAt = t.CreatedUtc
            })
            .ToListAsync();
    }

    public async Task<ClientTask?> GetByIdAsync(int id)
    {
        return await _db.ClientTasks
            .Include(t => t.Client)
            .Include(t => t.Site)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<ClientTask> CreateAsync(CreateClientTaskVm vm, string createdByUserId)
    {
        var task = new ClientTask
        {
            ClientId = vm.ClientId,
            SiteId = vm.SiteId,
            Title = vm.Title,
            Description = vm.Description,
            Priority = vm.Priority,
            Status = ClientTaskStatus.Open,
            DueDateUtc = vm.DueDateLocal.HasValue
                ? DateTime.SpecifyKind(vm.DueDateLocal.Value, DateTimeKind.Local).ToUniversalTime()
                : null,
            CreatedUtc = DateTime.UtcNow,
            CreatedByUserId = createdByUserId
        };

        _db.ClientTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    public async Task<(bool Found, string Title)> UpdateAsync(int id, EditClientTaskVm input)
    {
        var task = await _db.ClientTasks.FindAsync(id);
        if (task is null) return (false, string.Empty);

        task.ClientId = input.ClientId;
        task.SiteId = input.SiteId;
        task.Title = input.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        task.Priority = input.Priority;
        // Only on an actual status change, so editing an old imported task doesn't reactivate it
        if (task.Status != input.Status)
            task.IsActive = input.Status is not (ClientTaskStatus.Completed or ClientTaskStatus.Cancelled);
        task.Status = input.Status;
        task.AssignedToUserId = input.AssignedToUserId;
        task.DueDateUtc = input.DueDateLocal.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(input.DueDateLocal.Value, SydneyTz)
            : null;

        var justCompleted = input.Status == ClientTaskStatus.Completed && task.CompletedUtc is null;
        if (justCompleted)
            task.CompletedUtc = DateTime.UtcNow;
        else if (input.Status != ClientTaskStatus.Completed)
            task.CompletedUtc = null;

        await _db.SaveChangesAsync();

        if (justCompleted)
            await CompleteLinkedScheduleAsync(task);

        return (true, task.Title);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var task = await _db.ClientTasks.FindAsync(id);
        if (task is null) return false;

        _db.ClientTasks.Remove(task);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CompleteAsync(int id)
    {
        var task = await _db.ClientTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return false;

        if (task.Status != ClientTaskStatus.Completed)
        {
            task.Status = ClientTaskStatus.Completed;
            task.CompletedUtc = DateTime.UtcNow;
            task.IsActive = false;
            await _db.SaveChangesAsync();

            await CompleteLinkedScheduleAsync(task);
        }

        return true;
    }

    // Completing a task generated from a routine maintenance schedule also
    // completes the schedule (advances NextRunDate, logs MaintenanceHistory)
    // so techs never have to separately visit the schedule to close it out.
    private async Task CompleteLinkedScheduleAsync(ClientTask task)
    {
        if (task.MaintenanceScheduleId is int scheduleId)
            await _scheduleService.CompleteAsync(scheduleId, notes: null);

        // Imported Uptick tasks: each schedule whose next occurrence this task covers
        var covered = await _db.MaintenanceSchedules
            .Where(s => s.CoveringTasks.Any(t => t.Id == task.Id) && s.Id != task.MaintenanceScheduleId)
            .Select(s => new { s.Id, s.NextRunDate })
            .ToListAsync();

        foreach (var s in covered.Where(s => ScheduleCoverage.Covers(task, s.NextRunDate)))
            await _scheduleService.CompleteAsync(s.Id, notes: task.Ref == null ? null : $"Task {task.Ref} completed");
    }

    public async Task<(bool Found, bool TechValid)> AssignTechAsync(int taskId, string? techUserId)
    {
        var task = await _db.ClientTasks.FindAsync(taskId);
        if (task is null) return (false, true);

        if (techUserId != null)
        {
            var user = await _userManager.FindByIdAsync(techUserId);
            if (user == null || !await _userManager.IsInRoleAsync(user, "Tech"))
                return (true, false);
        }

        task.AssignedToUserId = techUserId;
        await _db.SaveChangesAsync();
        return (true, true);
    }
}
