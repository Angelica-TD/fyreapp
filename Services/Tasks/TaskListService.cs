using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.ViewModels.Lists;
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

    // Due between these dates (inclusive), e.g. a month of routine tasks
    public DateTime? DueFrom { get; set; }
    public DateTime? DueTo { get; set; }

    // "" any, "none" unassigned (in FyreApp and in Uptick), or a technician's Uptick user id
    // ("me" is turned into the signed-in user's by the controller)
    public string? Technician { get; set; }

    public int Page { get; set; } = 1;
}

public record TaskListItem(
    int Id, string? DisplayRef, string? Category, string Title, string ClientName, string SiteAddress,
    string Priority, string Status, DateTime? DueDateUtc,
    // The technician (an Uptick user), and who Uptick has an imported task assigned to when that's
    // someone else (or someone not among FyreApp's Uptick users)
    int? AssignedToId, string? AssignedToName, string? UptickTechnician);

public interface ITaskListService
{
    Task<(int Total, int Page, List<TaskListItem> Items, List<string> Categories)> SearchAsync(TaskFilter filter, CancellationToken ct = default);
    Task<byte[]> CsvAsync(TaskFilter filter, CancellationToken ct = default);

    // Tasks matching the search alone, whatever the other filters (active or not, any category, …): said when the
    // filters hide every match
    Task<int> CountSearchAsync(string search, CancellationToken ct = default);

    // Edit → Change status. Completing goes through the task service, so linked schedules roll forward as usual.
    Task<int> SetStatusAsync(BulkSelection selection, TaskFilter filter, ClientTaskStatus status, CancellationToken ct = default);

    // Edit → Assign technician (an Uptick user id; null = unassign). Returns null when there's no such
    // Uptick user, or they're a client portal user.
    Task<int?> AssignAsync(BulkSelection selection, TaskFilter filter, int? technicianId, CancellationToken ct = default);
}

// Tasks listed, filtered and edited like Uptick's Tasks page (React list on /Task)
public class TaskListService : ITaskListService
{
    private readonly AppDbContext _db;
    private readonly IClientTaskService _tasks;

    public TaskListService(AppDbContext db, IClientTaskService tasks)
    {
        _db = db;
        _tasks = tasks;
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

        if (filter.DueFrom is DateTime from)
        {
            var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            q = q.Where(t => t.DueDateUtc >= fromUtc);
        }
        if (filter.DueTo is DateTime to)
        {
            var beforeUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            q = q.Where(t => t.DueDateUtc < beforeUtc);
        }

        if (filter.Technician == "none")
            q = q.Where(t => t.AssignedToUptickUserId == null && t.UptickTechnician == null);
        else if (!string.IsNullOrWhiteSpace(filter.Technician))
        {
            var technicianId = int.TryParse(filter.Technician, out var id) ? id : -1;
            q = q.Where(t => t.AssignedToUptickUserId == technicianId);
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
            .OrderBy(t => t.UptickId != null)
            .ThenByDescending(t => t.UptickId == null ? t.Id : 0)
            .ThenByDescending(t => t.UptickId);
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
                t.Id, t.Ref ?? (t.UptickId != null ? t.UptickId.ToString() : t.FyreRef), t.Category, t.Title, t.Client.Name,
                t.Property.AddressDisplay ?? t.Property.Name, t.Priority.ToString(), t.Status.ToString(), t.DueDateUtc,
                t.AssignedToUptickUserId, t.AssignedTo != null ? t.AssignedTo.Name : null, t.UptickTechnician))
            .ToListAsync(ct);
        items = items.Select(i => i with { UptickTechnician = OtherUptickTechnician(i.AssignedToName, i.UptickTechnician) }).ToList();

        var categories = await _db.ClientTasks.AsNoTracking()
            .Where(t => t.Category != null)
            .Select(t => t.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return (total, page, items, categories);
    }

    public Task<int> CountSearchAsync(string search, CancellationToken ct = default) =>
        Filtered(new TaskFilter { Search = search, Active = null }).CountAsync(ct);

    public async Task<byte[]> CsvAsync(TaskFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(t => new
            {
                Ref = t.Ref ?? (t.UptickId != null ? t.UptickId.ToString() : t.FyreRef), t.Category, t.Status, t.IsActive, t.Title, t.Priority,
                t.DueDateUtc, t.CreatedUtc, t.CompletedUtc,
                Client = t.Client.Name, PropertyRef = t.Property.Ref ?? t.Property.FyreRef, Property = t.Property.Name,
                AssignedTo = t.AssignedTo != null ? t.AssignedTo.Name : null, t.UptickTechnician
            })
            .ToListAsync(ct);

        return CsvExport.Build(
            new[] { "Ref", "Category", "Status", "Active", "Task", "Priority", "Due", "Created", "Completed",
                    "Client", "Property ref", "Property", "Assigned to", "Uptick technician" },
            rows.Select(r => new[]
            {
                r.Ref, r.Category, r.Status.ToString(), r.IsActive ? "Yes" : "No", r.Title, r.Priority.ToString(),
                CsvExport.Date(r.DueDateUtc), CsvExport.Date(r.CreatedUtc.ToSydney()), CsvExport.Date(r.CompletedUtc?.ToSydney()),
                r.Client, r.PropertyRef, r.Property, r.AssignedTo, r.UptickTechnician
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

    public async Task<int?> AssignAsync(BulkSelection selection, TaskFilter filter, int? technicianId, CancellationToken ct = default)
    {
        if (technicianId != null)
        {
            if (!await TechnicianMatch.IsAssignableAsync(_db, technicianId.Value, ct)) return null;
        }

        var tasks = await Selected(selection, filter).ToListAsync(ct);
        foreach (var t in tasks) t.AssignedToUptickUserId = technicianId;
        await _db.SaveChangesAsync(ct);
        return tasks.Count;
    }

    // Uptick's technician for an imported task, unless that's who it's assigned to anyway
    public static string? OtherUptickTechnician(string? assignedToName, string? uptickTechnician) =>
        uptickTechnician != null && assignedToName != null &&
        string.Equals(UserManagement.UptickLoginSync.BareName(assignedToName), UserManagement.UptickLoginSync.BareName(uptickTechnician), StringComparison.OrdinalIgnoreCase)
            ? null
            : uptickTechnician;
}
