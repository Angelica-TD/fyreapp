using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

[Authorize]
public class TaskController : Controller
{
    private readonly AppDbContext _db;

    public TaskController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Create(int clientId, int siteId)
    {
        // Validate the site belongs to the client
        var valid = await _db.Sites.AsNoTracking()
            .AnyAsync(s => s.Id == siteId && s.ClientId == clientId);

        if (!valid) return NotFound();

        var vm = new CreateClientTaskVm
        {
            ClientId = clientId,
            SiteId = siteId
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateClientTaskVm vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var site = await _db.Sites
            .Include(s => s.Client)
            .FirstOrDefaultAsync(s => s.Id == vm.SiteId && s.ClientId == vm.ClientId);

        if (site == null) return NotFound();

        DateTime? dueUtc = null;
        if (vm.DueDateLocal.HasValue)
            dueUtc = DateTime.SpecifyKind(vm.DueDateLocal.Value, DateTimeKind.Local).ToUniversalTime();

        var task = new ClientTask
        {
            ClientId = vm.ClientId,
            SiteId = vm.SiteId,
            Title = vm.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
            Priority = vm.Priority,
            Status = ClientTaskStatus.Open,
            DueDateUtc = dueUtc,
            CreatedUtc = DateTime.UtcNow,
            CreatedByUserId = User?.Identity?.Name // swap to user id claim if you store it
        };

        _db.ClientTasks.Add(task);
        await _db.SaveChangesAsync();

        // Nice UX: redirect back to the client or site details page
        return RedirectToAction("Details", "Sites", new { id = vm.SiteId });
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
}
