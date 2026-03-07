using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services;

public class ClientTaskService : IClientTaskService
{
    private readonly AppDbContext _db;
    
    private static readonly TimeZoneInfo SydneyTz =
        TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public ClientTaskService(AppDbContext db)
    {
        _db = db;
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
        task.Status = input.Status;
        task.DueDateUtc = input.DueDateLocal.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(input.DueDateLocal.Value, SydneyTz)
            : null;

        if (input.Status == ClientTaskStatus.Completed && task.CompletedUtc is null)
            task.CompletedUtc = DateTime.UtcNow;
        else if (input.Status != ClientTaskStatus.Completed)
            task.CompletedUtc = null;

        await _db.SaveChangesAsync();
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
}
