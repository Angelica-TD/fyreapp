using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class AssetCatalogueController : Controller
{
    private readonly AppDbContext _db;

    public AssetCatalogueController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var items = await _db.AssetCatalogue.OrderBy(a => a.Name).ToListAsync();
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, CancellationToken ct)
    {
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

        TempData["Success"] = $"'{name}' added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.AssetCatalogue.FindAsync(id);
        if (item == null) return NotFound();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string name, CancellationToken ct)
    {
        var item = await _db.AssetCatalogue.FindAsync(new object[] { id }, ct);
        if (item == null) return NotFound();

        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Name is required.");
            return View(item);
        }

        if (await _db.AssetCatalogue.AnyAsync(a => a.Id != id && a.Name.ToLower() == name.ToLower(), ct))
        {
            ModelState.AddModelError(nameof(name), $"'{name}' already exists in the catalogue.");
            return View(item);
        }

        item.Name = name;
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Updated to '{name}'.";
        return RedirectToAction("Index", "Dev");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _db.AssetCatalogue.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        TempData[deleted > 0 ? "Success" : "Error"] = deleted > 0 ? "Item deleted." : "Item not found.";
        return RedirectToAction(nameof(Index));
    }
}
