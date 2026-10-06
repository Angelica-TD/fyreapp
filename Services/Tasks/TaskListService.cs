using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Tasks;

// Tasks list filters; defaults match Uptick's Tasks page: active tasks, any category, any status
public class TaskFilter
{
    public string? Search { get; set; }
    public bool? Active { get; set; } = true;
    public List<string> Category { get; set; } = new();
    public bool CategoryNot { get; set; }
    public List<ClientTaskStatus> Status { get; set; } = new();
    public bool StatusNot { get; set; }
    public int Page { get; set; } = 1;
}

public record TaskListItem(
    int Id, string? DisplayRef, string? Category, string Title, string ClientName, string SiteAddress,
    string Priority, string Status, DateTime? DueDateUtc);

public interface ITaskListService
{
    Task<(int Total, int Page, List<TaskListItem> Items, List<string> Categories)> SearchAsync(TaskFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(TaskFilter filter, CancellationToken ct = default);

    // Edit → Change status. Completing goes through the task service, so linked schedules roll forward as usual.
    Task<int> SetStatusAsync(BulkSelection selection, TaskFilter filter, ClientTaskStatus status, CancellationToken ct = default);

    // Edit → Assign technician (null = unassign). Returns null when the user isn't a technician.
    Task<int?> AssignAsync(BulkSelection selection, TaskFilter filter, string? techUserId, CancellationToken ct = default);
}

// Tasks listed, filtered and edited like Uptick's Tasks page (React list on /Task)
public class TaskListService : ITaskListService
{
    private readonly AppDbContext _db;
    private readonly IClientTaskService _tasks;
    private readonly UserManager<ApplicationUser> _users;

    public TaskListService(AppDbContext db, IClientTaskService tasks, UserManager<ApplicationUser> users)
    {
        _db = db;
        _tasks = tasks;
        _users = users;
    }

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<ClientTask> Filtered(TaskFilter filter)
    {
        var q = _db.ClientTasks.AsQueryable();

        if (filter.Active is bool active)
            q = q.Where(t => t.IsActive == active);

        if (filter.Category.Count > 0)
        {
            var categories = filter.Category.ToList();
            q = filter.CategoryNot
                ? q.Where(t => t.Category == null || !categories.Contains(t.Category))
                : q.Where(t => t.Category != null && categories.Contains(t.Category));
        }

        if (filter.Status.Count > 0)
        {
            var statuses = filter.Status.ToList();
            q = filter.StatusNot ? q.Where(t => !statuses.Contains(t.Status)) : q.Where(t => statuses.Contains(t.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(t =>
                t.Title.ToLower().Contains(term) ||
                t.Client.Name.ToLower().Contains(term) ||
                (t.FyreRef != null && t.FyreRef.ToLower() == term) ||
                (t.Ref != null && t.Ref.ToLower() == term));
        }

        // Newest first, as Uptick lists them: made in FyreApp, then Uptick IDs numerically
        return q
            .OrderBy(t => t.ExternalId != null)
            .ThenByDescending(t => t.ExternalId == null ? t.Id : 0)
            .ThenByDescending(t => (t.ExternalId ?? "").Length)
            .ThenByDescending(t => t.ExternalId);
    }

    private IQueryable<ClientTask> Selected(BulkSelection selection, TaskFilter filter)
    {
        if (selection.AllMatching) return Filtered(filter);
        var ids = selection.Ids.ToList();
        return _db.ClientTasks.Where(t => ids.Contains(t.Id));
    }

    public async Task<(int Total, int Page, List<TaskListItem> Items, List<string> Categories)> SearchAsync(TaskFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();
        var total = await q.CountAsync(ct);
        var page = ListFilters.ClampPage(filter.Page, total);

        var items = await q
            .Skip((page - 1) * ListFilters.PageSize)
            .Take(ListFilters.PageSize)
            .Select(t => new TaskListItem(
                t.Id, t.Ref ?? t.ExternalId ?? t.FyreRef, t.Category, t.Title, t.Client.Name,
                t.Site.AddressDisplay ?? t.Site.Name, t.Priority.ToString(), t.Status.ToString(), t.DueDateUtc))
            .ToListAsync(ct);

        var categories = await _db.ClientTasks.AsNoTracking()
            .Where(t => t.Category != null)
            .Select(t => t.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return (total, page, items, categories);
    }

    public async Task<byte[]> CsvAsync(TaskFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(t => new
            {
                Ref = t.Ref ?? t.ExternalId ?? t.FyreRef, t.Category, t.Status, t.IsActive, t.Title, t.Priority,
                t.DueDateUtc, t.CreatedUtc, t.CompletedUtc,
                Client = t.Client.Name, PropertyRef = t.Site.ExternalId ?? t.Site.FyreRef, Property = t.Site.Name,
                AssignedTo = t.AssignedTo != null ? t.AssignedTo.FirstName + " " + t.AssignedTo.LastName : null
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Ref", "Category", "Status", "Active", "Task", "Priority", "Due", "Created", "Completed",
                    "Client", "Property ref", "Property", "Assigned to" },
            rows.Select(r => new[]
            {
                r.Ref, r.Category, r.Status.ToString(), r.IsActive ? "Yes" : "No", r.Title, r.Priority.ToString(),
                CsvExport.Date(r.DueDateUtc), CsvExport.Date(r.CreatedUtc.ToSydney()), CsvExport.Date(r.CompletedUtc?.ToSydney()),
                r.Client, r.PropertyRef, r.Property, r.AssignedTo
            }));
    }

    public async Task<int> SetStatusAsync(BulkSelection selection, TaskFilter filter, ClientTaskStatus status, CancellationToken ct = default)
    {
        var tasks = await Selected(selection, filter).ToListAsync(ct);

        if (status == ClientTaskStatus.Completed)
        {
            foreach (var t in tasks.Where(t => t.Status != ClientTaskStatus.Completed))
                await _tasks.CompleteAsync(t.Id);
            return tasks.Count;
        }

        foreach (var t in tasks)
        {
            if (t.Status == status) continue;
            t.Status = status;
            t.IsActive = status != ClientTaskStatus.Cancelled;
            t.CompletedUtc = null;
        }
        await _db.SaveChangesAsync(ct);
        return tasks.Count;
    }

    public async Task<int?> AssignAsync(BulkSelection selection, TaskFilter filter, string? techUserId, CancellationToken ct = default)
    {
        if (techUserId != null)
        {
            var user = await _users.FindByIdAsync(techUserId);
            if (user == null || !await _users.IsInRoleAsync(user, "Tech")) return null;
        }

        var tasks = await Selected(selection, filter).ToListAsync(ct);
        foreach (var t in tasks) t.AssignedToUserId = techUserId;
        await _db.SaveChangesAsync(ct);
        return tasks.Count;
    }
}
