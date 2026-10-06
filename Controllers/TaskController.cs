using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services;
using FyreApp.ViewModels.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;


namespace FyreApp.Controllers;

[Authorize]
public class TaskController : Controller
{
    private readonly AppDbContext _db;
    private readonly IClientTaskService _taskService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly FyreApp.Services.Tasks.ITaskListService _taskList;

    public TaskController(AppDbContext db, IClientTaskService taskService, UserManager<ApplicationUser> userManager,
        FyreApp.Services.Tasks.ITaskListService taskList)
    {
        _taskList = taskList;
        _db = db;
        _taskService = taskService;
        _userManager = userManager;
    }


    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var techs = (await GetTechSelectListAsync()).Where(t => t.Value != "").Select(t => (t.Value, t.Text)).ToList();
        return View(new TaskIndexVm { Techs = techs });
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var clients = await _db.Clients
            .Where(c => c.Active)
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            })
            .ToListAsync();

        var vm = new CreateClientTaskFormVm
        {
            Clients = clients,
            Sites = new List<SelectListItem>()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateClientTaskVm task)
    {
        var siteExists = await _db.Sites
            .AsNoTracking()
            .AnyAsync(s => s.Id == task.SiteId && s.ClientId == task.ClientId);

        if (!siteExists)
            ModelState.AddModelError(nameof(CreateClientTaskVm.SiteId), "Selected site does not belong to selected client.");

        if (!ModelState.IsValid)
        {
            var vm = new CreateClientTaskFormVm
            {
                Task = task,
                Clients = await _db.Clients
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Name
                    })
                    .ToListAsync(),
                Sites = await _db.Sites
                    .AsNoTracking()
                    .Where(s => s.ClientId == task.ClientId)
                    .OrderBy(s => s.Name)
                    .Select(s => new SelectListItem
                    {
                        Value = s.Id.ToString(),
                        Text = s.Name
                    })
                    .ToListAsync()
            };

            return View(vm);
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        DateTime? dueUtc = null;
        if (task.DueDateLocal.HasValue)
            dueUtc = TimeZoneInfo.ConvertTimeToUtc(task.DueDateLocal.Value, tz);

        var entity = new ClientTask
        {
            ClientId = task.ClientId,
            SiteId = task.SiteId,
            Title = task.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim(),
            Priority = task.Priority,
            Status = ClientTaskStatus.Open,
            DueDateUtc = dueUtc,
            CreatedUtc = DateTime.UtcNow
        };

        _db.ClientTasks.Add(entity);
        await _db.SaveChangesAsync();

        return RedirectToAction("Details", "Task", new { id = entity.Id });
    }

    [HttpGet]
    public async Task<IActionResult> PropertiesForClient(int clientId)
    {
        var sites = await _db.Sites
            .AsNoTracking()
            .Where(s => s.ClientId == clientId)
            .OrderBy(s => s.Name)
            .Select(s => new { id = s.Id, name = s.Name })
            .ToListAsync();

        return Json(sites);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound();

        await _taskService.CompleteAsync(id);

        return RedirectToAction("Details", "Property", new { id = task.SiteId });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var clientTask = await _db.ClientTasks
            .Include(s => s.Client)
            .Include(s => s.Site)
            .Include(t => t.AssignedTo)
            .Include(t => t.CoveredSchedules).ThenInclude(s => s.MaintenanceInterval)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (clientTask == null)
            return NotFound();

        return View(clientTask);
    }


    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();

        var sydneyTz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

        return View(new EditClientTaskFormVm
        {
            Task = new EditClientTaskVm
            {
                Id = task.Id,
                ClientId = task.ClientId,
                SiteId = task.SiteId,
                Title = task.Title,
                Description = task.Description,
                Priority = task.Priority,
                Status = task.Status,
                AssignedToUserId = task.AssignedToUserId,
                DueDateLocal = task.DueDateUtc.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(task.DueDateUtc.Value, sydneyTz)
                    : null
            },
            Clients = await _db.Clients
                .Where(c => c.Active)
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToListAsync(),
            Sites = await _db.Sites
                .AsNoTracking()
                .Where(s => s.ClientId == task.ClientId)
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name })
                .ToListAsync(),
            Techs = await GetTechSelectListAsync(task.AssignedToUserId)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditClientTaskFormVm formVm)
    {
        var input = formVm.Task;

        if (id != input.Id) return BadRequest();

        var siteExists = await _db.Sites
            .AsNoTracking()
            .AnyAsync(s => s.Id == input.SiteId && s.ClientId == input.ClientId);

        if (!siteExists)
            ModelState.AddModelError("Task.SiteId", "Selected site does not belong to selected client.");

        if (!ModelState.IsValid)
        {
            return View(new EditClientTaskFormVm
            {
                Task = input,
                Clients = await _db.Clients
                    .Where(c => c.Active)
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                    .ToListAsync(),
                Sites = await _db.Sites
                    .AsNoTracking()
                    .Where(s => s.ClientId == input.ClientId)
                    .OrderBy(s => s.Name)
                    .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name })
                    .ToListAsync(),
                Techs = await GetTechSelectListAsync(input.AssignedToUserId)
            });
        }

        var (found, title) = await _taskService.UpdateAsync(id, input);
        if (!found) return NotFound();

        TempData["Success"] = $"Task \"{title}\" updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var task = await _db.ClientTasks
            .Include(t => t.Client)
            .Include(t => t.Site)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        return View(task);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var deleted = await _taskService.DeleteAsync(id);
        if (!deleted) return NotFound();

        TempData["Success"] = "Task deleted.";
        return RedirectToAction(nameof(Index));
    }

    // Tasks list (React). Defaults match Uptick's Tasks page: active tasks, any category, any status.
    // category / status are "is" lists, or "is not" with categoryNot / statusNot.
    private static FyreApp.Services.Tasks.TaskFilter TaskFilterFrom(
        string? search, string active, List<string>? category, bool categoryNot, List<ClientTaskStatus>? status, bool statusNot) => new()
    {
        Search = search,
        Active = active switch { "yes" => true, "no" => false, _ => null },
        Category = category ?? new(),
        CategoryNot = categoryNot,
        Status = status ?? new(),
        StatusNot = statusNot
    };

    [HttpGet("/api/tasks")]
    public async Task<IActionResult> ApiSearch(
        string? search, string active = "yes",
        [FromQuery] List<string>? category = null, bool categoryNot = false,
        [FromQuery] List<ClientTaskStatus>? status = null, bool statusNot = false,
        int page = 1, CancellationToken ct = default)
    {
        var filter = TaskFilterFrom(search, active, category, categoryNot, status, statusNot);
        filter.Page = page;
        var (total, current, items, categories) = await _taskList.SearchAsync(filter, ct);
        return Json(new { total, page = current, pages = FyreApp.ViewModels.Lists.ListFilters.Pages(total), items, categories });
    }

    // Every task matching the current filters, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(
        string? search, string active = "yes",
        [FromQuery] List<string>? category = null, bool categoryNot = false,
        [FromQuery] List<ClientTaskStatus>? status = null, bool statusNot = false, CancellationToken ct = default) =>
        File(await _taskList.CsvAsync(TaskFilterFrom(search, active, category, categoryNot, status, statusNot), ct),
            "text/csv", FyreApp.Infrastructure.CsvExport.FileName("tasks"));

    public class TaskBulkRequest
    {
        public List<int> Ids { get; set; } = new();
        public bool AllMatching { get; set; }
        public string? Search { get; set; }
        public string Active { get; set; } = "yes";
        public List<string> Category { get; set; } = new();
        public bool CategoryNot { get; set; }
        // Status names as the list sends them, e.g. "InProgress"
        public List<string> Status { get; set; } = new();
        public bool StatusNot { get; set; }

        // "status" (NewStatus) or "assign" (TechUserId; empty = unassign)
        public string BulkAction { get; set; } = "";
        public string? NewStatus { get; set; }
        public string? TechUserId { get; set; }
    }

    // Edit (React): change status or assign a technician, for the ticked tasks or all matching the filters
    [HttpPost("/api/tasks/bulk")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApiBulk([FromBody] TaskBulkRequest req, CancellationToken ct = default)
    {
        var selection = FyreApp.ViewModels.Lists.BulkSelection.From(req.Ids, req.AllMatching);
        var statuses = req.Status.Select(s => Enum.TryParse<ClientTaskStatus>(s, out var st) ? st : (ClientTaskStatus?)null).OfType<ClientTaskStatus>().ToList();
        var filter = TaskFilterFrom(req.Search, req.Active, req.Category, req.CategoryNot, statuses, req.StatusNot);

        switch (req.BulkAction)
        {
            case "status" when Enum.TryParse<ClientTaskStatus>(req.NewStatus, out var newStatus):
                var changed = await _taskList.SetStatusAsync(selection, filter, newStatus, ct);
                return Json(new { message = $"Set {FyreApp.Infrastructure.ListControllerExtensions.Plural(changed, "task", "tasks")} to {newStatus}." });
            case "assign":
                var tech = string.IsNullOrWhiteSpace(req.TechUserId) ? null : req.TechUserId;
                var assigned = await _taskList.AssignAsync(selection, filter, tech, ct);
                return assigned is int n
                    ? Json(new { message = tech == null
                        ? $"Unassigned {FyreApp.Infrastructure.ListControllerExtensions.Plural(n, "task", "tasks")}."
                        : $"Assigned {FyreApp.Infrastructure.ListControllerExtensions.Plural(n, "task", "tasks")}." })
                    : BadRequest(new { message = "That user isn't a technician." });
            default:
                return BadRequest(new { message = "Choose what to do." });
        }
    }

    private async Task<List<SelectListItem>> GetTechSelectListAsync(string? selectedId = null)
    {
        var techs = await _userManager.GetUsersInRoleAsync("Tech");
        var items = techs
            .Where(t => t.IsActive)
            .OrderBy(t => t.LastName)
            .Select(t => new SelectListItem
            {
                Value = t.Id,
                Text = $"{t.FirstName} {t.LastName}",
                Selected = t.Id == selectedId
            })
            .ToList();

        items.Insert(0, new SelectListItem { Value = "", Text = "— Unassigned —" });
        return items;
    }

}
