using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Dev;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class DevController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DevController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        if (!_env.IsDevelopment()) return NotFound();

        var vm = new DevIndexVm
        {
            Clients = await _db.Clients
                .OrderBy(c => c.Name)
                .Select(c => new DevClientRow(
                    c.Id, c.Name,
                    c.Sites.Count,
                    _db.ClientTasks.Count(t => t.ClientId == c.Id)))
                .ToListAsync(),

            Sites = await _db.Sites
                .OrderBy(s => s.Client.Name).ThenBy(s => s.Name)
                .Select(s => new DevSiteRow(
                    s.Id, s.Name, s.Client.Name,
                    s.AddressDisplay ?? s.AddressLine1,
                    s.Assets.Count))
                .ToListAsync(),

            Assets = await _db.Assets
                .OrderBy(a => a.Site.Client.Name).ThenBy(a => a.Site.Name).ThenBy(a => a.Name)
                .Select(a => new DevAssetRow(a.Id, a.Name, a.Site.Name, a.Site.Client.Name))
                .ToListAsync(),

            Tasks = await _db.ClientTasks
                .OrderBy(t => t.Client.Name).ThenBy(t => t.Title)
                .Select(t => new DevTaskRow(
                    t.Id, t.Title, t.Client.Name,
                    t.Site != null ? t.Site.Name : null,
                    t.Status.ToString()))
                .ToListAsync(),

            Catalogue = await _db.AssetCatalogue
                .OrderBy(a => a.Name)
                .Select(a => new DevCatalogueRow(a.Id, a.Name))
                .ToListAsync(),
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string entityType, int[] ids, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        if (ids is null || ids.Length == 0)
        {
            TempData["Info"] = "Nothing selected.";
            return RedirectToAction(nameof(Index));
        }

        int deleted = 0;

        switch (entityType)
        {
            case "Client":
                deleted = await _db.Clients
                    .Where(c => ids.Contains(c.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            case "Site":
                deleted = await _db.Sites
                    .Where(s => ids.Contains(s.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            case "Asset":
                deleted = await _db.Assets
                    .Where(a => ids.Contains(a.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            case "Task":
                deleted = await _db.ClientTasks
                    .Where(t => ids.Contains(t.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            default:
                TempData["Error"] = $"Unknown entity type: {entityType}";
                return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Deleted {deleted} {entityType}(s).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CatalogueCreate(string name, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.AssetCatalogue.AnyAsync(a => a.Name.ToLower() == name.ToLower(), ct))
        {
            TempData["Error"] = $"'{name}' already exists in the catalogue.";
            return RedirectToAction(nameof(Index));
        }

        _db.AssetCatalogue.Add(new AssetCatalogue { Name = name });
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"'{name}' added to catalogue.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CatalogueDelete(int id, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        await _db.AssetCatalogue.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        TempData["Success"] = "Item deleted from catalogue.";
        return RedirectToAction(nameof(Index));
    }
}
