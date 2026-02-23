using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

[Authorize]
public class TaskController : Controller
{
    private readonly AppDbContext _db;

    public TaskController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var tasks = await _db.ClientTasks
            // .Where(c => c.Active)
            .AsNoTracking()
            .OrderBy(c => c.DueDateUtc)
            // .Select(c => new SelectListItem
            // {
            //     Value = c.Id.ToString(),
            //     Text = c.Name
            // })
            .ToListAsync();

        // var vm = new CreateClientTaskFormVm
        // {
        //     Clients = clients,
        //     Sites = new List<SelectListItem>()
        // };

        return View(tasks);
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
        // Validate client/site relationship (don't trust posted IDs)
        var siteExists = await _db.Sites
            .AsNoTracking()
            .AnyAsync(s => s.Id == task.SiteId && s.ClientId == task.ClientId);

        if (!siteExists)
            ModelState.AddModelError(nameof(CreateClientTaskVm.SiteId), "Selected site does not belong to selected client.");

        if (!ModelState.IsValid)
        {
            // Rebuild dropdowns for the form (required when returning the view)
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

        // Convert local due date to UTC
        DateTime? dueUtc = null;
        if (task.DueDateLocal.HasValue)
        {
            // users are AU/Sydney, convert using that timezone.
            // Better: store timezone per user later.
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
            dueUtc = TimeZoneInfo.ConvertTimeToUtc(task.DueDateLocal.Value, tz);
        }

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

        return RedirectToAction("Details", "Task", new { id = task.SiteId });
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
        var task = await _db.ClientTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound();

        if (task.Status != ClientTaskStatus.Completed)
        {
            task.Status = ClientTaskStatus.Completed;
            task.CompletedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction("Details", "Sites", new { id = task.SiteId });
    }

    // GET: /Task/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var clientTask = await _db.ClientTasks
            .Include(s => s.Client)
            // .Include(s => s.Assets)
            //     .ThenInclude(a => a.AssetTypes)
            // .Include(s => s.MaintenanceSchedules)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (clientTask == null)
            return NotFound();

        // ViewBag.Intervals = await _db.MaintenanceIntervals.ToListAsync();

        return View(clientTask);
    }
}
