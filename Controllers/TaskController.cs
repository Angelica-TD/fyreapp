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
        var me = await _userManager.GetUserAsync(User);
        return View(new TaskIndexVm
        {
            Techs = techs,
            CanFilterMine = me?.UptickUserId != null,
            // Technicians open the list on their own tasks
            DefaultTechnician = FyreApp.Auth.TechAccessFilter.IsTech(User) ? "me" : ""
        });
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
            Properties = new List<SelectListItem>()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateClientTaskVm task)
    {
        var siteExists = await _db.Properties
            .AsNoTracking()
            .AnyAsync(s => s.Id == task.PropertyId && s.ClientId == task.ClientId);

        if (!siteExists)
            ModelState.AddModelError(nameof(CreateClientTaskVm.PropertyId), "Selected site does not belong to selected client.");

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
                Properties = await _db.Properties
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
            PropertyId = task.PropertyId,
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
        var sites = await _db.Properties
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

        return RedirectToAction("Details", "Property", new { id = task.PropertyId });
    }

    // The task page, laid out like Uptick's (Task and Work tabs)
    [HttpGet]
    public async Task<IActionResult> Details(int id, string? tab, [FromServices] FyreApp.Services.Tasks.ITaskPageService pages, CancellationToken ct = default)
    {
        var vm = await pages.LoadAsync(id, tab, ct);
        return vm == null ? NotFound() : View(vm);
    }

    // The page's "Assign field staff": the task's technician, an Uptick user (blank = unassign)
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Assign(int id, int? technicianId)
    {
        var (found, valid) = await _taskService.AssignTechAsync(id, technicianId);
        if (!found) return NotFound();
        TempData[valid ? "Success" : "Error"] = valid
            ? (technicianId == null ? "Technician removed." : "Technician assigned.")
            : "That Uptick user can't be assigned tasks.";
        return RedirectToAction(nameof(Details), new { id });
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
                PropertyId = task.PropertyId,
                Title = task.Title,
                Description = task.Description,
                Priority = task.Priority,
                Status = task.Status,
                AssignedToUptickUserId = task.AssignedToUptickUserId,
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
            Properties = await _db.Properties
                .AsNoTracking()
                .Where(s => s.ClientId == task.ClientId)
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name })
                .ToListAsync(),
            Techs = await GetTechSelectListAsync(task.AssignedToUptickUserId)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditClientTaskFormVm formVm)
    {
        var input = formVm.Task;

        if (id != input.Id) return BadRequest();

        var siteExists = await _db.Properties
            .AsNoTracking()
            .AnyAsync(s => s.Id == input.PropertyId && s.ClientId == input.ClientId);

        if (!siteExists)
            ModelState.AddModelError("Task.PropertyId", "Selected site does not belong to selected client.");

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
                Properties = await _db.Properties
                    .AsNoTracking()
                    .Where(s => s.ClientId == input.ClientId)
                    .OrderBy(s => s.Name)
                    .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name })
                    .ToListAsync(),
                Techs = await GetTechSelectListAsync(input.AssignedToUptickUserId)
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
            .Include(t => t.Property)
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
        string? search, string active, List<string>? category, bool categoryNot, List<ClientTaskStatus>? status, bool statusNot,
        DateTime? dueFrom = null, DateTime? dueTo = null, string? technician = null) => new()
    {
        Search = search,
        DueFrom = dueFrom,
        DueTo = dueTo,
        Technician = technician,
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
        DateTime? dueFrom = null, DateTime? dueTo = null, string? technician = null,
        int page = 1, CancellationToken ct = default)
    {
        var filter = TaskFilterFrom(search, active, category, categoryNot, status, statusNot, dueFrom, dueTo, await ResolveTechnicianAsync(technician));
        filter.Page = page;
        var (total, current, items, categories) = await _taskList.SearchAsync(filter, ct);
        return Json(new { total, page = current, pages = FyreApp.ViewModels.Lists.ListFilters.Pages(total), items, categories });
    }

    // Every task matching the current filters, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(
        string? search, string active = "yes",
        [FromQuery] List<string>? category = null, bool categoryNot = false,
        [FromQuery] List<ClientTaskStatus>? status = null, bool statusNot = false,
        DateTime? dueFrom = null, DateTime? dueTo = null, string? technician = null, CancellationToken ct = default) =>
        File(await _taskList.CsvAsync(TaskFilterFrom(search, active, category, categoryNot, status, statusNot, dueFrom, dueTo, await ResolveTechnicianAsync(technician)), ct),
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
        public DateTime? DueFrom { get; set; }
        public DateTime? DueTo { get; set; }
        public string? Technician { get; set; }

        // "status" (NewStatus) or "assign" (TechnicianId, an Uptick user id; empty = unassign)
        public string BulkAction { get; set; } = "";
        public string? NewStatus { get; set; }
        public string? TechnicianId { get; set; }
    }

    // Edit (React): change status or assign a technician, for the ticked tasks or all matching the filters
    [HttpPost("/api/tasks/bulk")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApiBulk([FromBody] TaskBulkRequest req, CancellationToken ct = default)
    {
        var selection = FyreApp.ViewModels.Lists.BulkSelection.From(req.Ids, req.AllMatching);
        var statuses = req.Status.Select(s => Enum.TryParse<ClientTaskStatus>(s, out var st) ? st : (ClientTaskStatus?)null).OfType<ClientTaskStatus>().ToList();
        var filter = TaskFilterFrom(req.Search, req.Active, req.Category, req.CategoryNot, statuses, req.StatusNot, req.DueFrom, req.DueTo, await ResolveTechnicianAsync(req.Technician));

        switch (req.BulkAction)
        {
            case "status" when Enum.TryParse<ClientTaskStatus>(req.NewStatus, out var newStatus):
                var changed = await _taskList.SetStatusAsync(selection, filter, newStatus, ct);
                return Json(new { message = $"Set {FyreApp.Infrastructure.ListControllerExtensions.Plural(changed, "task", "tasks")} to {newStatus}." });
            case "assign":
                int? tech = int.TryParse(req.TechnicianId, out var technicianId) ? technicianId : null;
                if (tech == null && !string.IsNullOrWhiteSpace(req.TechnicianId))
                    return BadRequest(new { message = "That isn't an Uptick user." });
                var assigned = await _taskList.AssignAsync(selection, filter, tech, ct);
                return assigned is int n
                    ? Json(new { message = tech == null
                        ? $"Unassigned {FyreApp.Infrastructure.ListControllerExtensions.Plural(n, "task", "tasks")}."
                        : $"Assigned {FyreApp.Infrastructure.ListControllerExtensions.Plural(n, "task", "tasks")}." })
                    : BadRequest(new { message = "That Uptick user can't be assigned tasks." });
            default:
                return BadRequest(new { message = "Choose what to do." });
        }
    }

    // Technicians are Uptick users: field technicians and contractors, plus whoever the task already has
    // (e.g. an office user Uptick assigned an imported task to)
    private async Task<List<SelectListItem>> GetTechSelectListAsync(int? selectedId = null)
    {
        var items = await _db.UptickUsers.AsNoTracking()
            .Where(u => u.LicenseType == "FIELD" || u.LicenseType == "CONTRACTOR" || u.Id == selectedId)
            .OrderBy(u => u.Name)
            .Select(u => new SelectListItem
            {
                Value = u.Id.ToString(),
                Text = u.LicenseType == "CONTRACTOR" ? u.Name + " (contractor)" : u.Name,
                Selected = u.Id == selectedId
            })
            .ToListAsync();

        items.Insert(0, new SelectListItem { Value = "", Text = "— Unassigned —" });
        return items;
    }

    // "me" in the technician filter: the Uptick user the signed-in login is linked to ("-1", no tasks, if none)
    // A technician only ever sees their own (Auth/TechAccess.cs)
    private async Task<string?> ResolveTechnicianAsync(string? technician)
    {
        if (technician != "me" && !FyreApp.Auth.TechAccessFilter.IsTech(User)) return technician;
        var me = await _userManager.GetUserAsync(User);
        return me?.UptickUserId?.ToString() ?? "-1";
    }

}
