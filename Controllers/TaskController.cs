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

    public TaskController(AppDbContext db, IClientTaskService taskService, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _taskService = taskService;
        _userManager = userManager;
    }


    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? client)
    {
        var q = _db.ClientTasks
            .Include(t => t.Client)
            .Include(t => t.Site)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ClientTaskStatus>(status, out var statusEnum))
            q = q.Where(t => t.Status == statusEnum);

        if (!string.IsNullOrWhiteSpace(client) && int.TryParse(client, out var clientId))
            q = q.Where(t => t.ClientId == clientId);

        var tasks = await q
            .OrderBy(t => t.DueDateUtc)
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

        return View(new TaskIndexVm
        {
            Tasks = tasks,
            StatusFilter = status,
            ClientFilter = client
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
    [HttpGet("/api/tasks")]
    public async Task<IActionResult> ApiSearch(
        string? search, string active = "yes",
        [FromQuery] List<string>? category = null, bool categoryNot = false,
        [FromQuery] List<ClientTaskStatus>? status = null, bool statusNot = false,
        int page = 1, CancellationToken ct = default)
    {
        const int pageSize = FyreApp.ViewModels.Lists.ListFilters.PageSize;
        var q = _db.ClientTasks.AsNoTracking().AsQueryable();

        if (active is "yes" or "no")
        {
            var isActive = active == "yes";
            q = q.Where(t => t.IsActive == isActive);
        }

        if (category is { Count: > 0 })
        {
            var categories = category.ToList();
            q = categoryNot
                ? q.Where(t => t.Category == null || !categories.Contains(t.Category))
                : q.Where(t => t.Category != null && categories.Contains(t.Category));
        }

        if (status is { Count: > 0 })
        {
            var statuses = status.ToList();
            q = statusNot ? q.Where(t => !statuses.Contains(t.Status)) : q.Where(t => statuses.Contains(t.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(t =>
                t.Title.ToLower().Contains(term) ||
                t.Client.Name.ToLower().Contains(term) ||
                (t.FyreRef != null && t.FyreRef.ToLower() == term) ||
                (t.Ref != null && t.Ref.ToLower() == term));
        }

        var total = await q.CountAsync(ct);
        page = FyreApp.ViewModels.Lists.ListFilters.ClampPage(page, total);

        // Newest first, as Uptick lists them: made in FyreApp, then Uptick IDs numerically
        var items = await q
            .OrderBy(t => t.ExternalId != null)
            .ThenByDescending(t => t.ExternalId == null ? t.Id : 0)
            .ThenByDescending(t => (t.ExternalId ?? "").Length)
            .ThenByDescending(t => t.ExternalId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                id          = t.Id,
                displayRef  = t.Ref ?? t.ExternalId ?? t.FyreRef,
                category    = t.Category,
                title       = t.Title,
                clientName  = t.Client.Name,
                siteAddress = t.Site.AddressDisplay ?? t.Site.Name,
                priority    = t.Priority.ToString(),
                status      = t.Status.ToString(),
                dueDateUtc  = t.DueDateUtc
            })
            .ToListAsync(ct);

        var categoryOptions = await _db.ClientTasks.AsNoTracking()
            .Where(t => t.Category != null)
            .Select(t => t.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return Json(new { total, page, pages = FyreApp.ViewModels.Lists.ListFilters.Pages(total), items, categories = categoryOptions });
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
