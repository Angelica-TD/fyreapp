using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using Microsoft.AspNetCore.Authorization;
using FyreApp.Services.Assets;
using FyreApp.ViewModels.Assets;

namespace FyreApp.Controllers
{
    public class AssetsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IAssetListService _assetList;
        private readonly IAssetPageService _page;

        public AssetsController(AppDbContext context, IAssetListService assetList, IAssetPageService page)
        {
            _context = context;
            _assetList = assetList;
            _page = page;
        }

        // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
        // submitted, what's in the query string is used as is, so a cleared filter means "any".
        private static AssetFilter Filter(
            string? search, string? active, List<int>? assetType, bool assetTypeIsNot, List<string>? propertyStatus, bool f)
        {
            var filter = new AssetFilter { Search = search };
            if (f)
            {
                filter.Active = active switch { "yes" => true, "no" => false, _ => null };
                filter.AssetTypeIds = assetType ?? new();
                filter.AssetTypeIsNot = assetTypeIsNot;
                filter.PropertyStatus = propertyStatus ?? new();
            }
            return filter;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(
            string? search, string? active, List<int>? assetType, bool assetTypeIsNot,
            List<string>? propertyStatus, int page = 1, bool f = false, CancellationToken ct = default)
        {
            var filter = Filter(search, active, assetType, assetTypeIsNot, propertyStatus, f);
            filter.Page = page;
            return View(await _assetList.SearchAsync(filter, ct));
        }

        // Every asset matching the current filters, as CSV; with site, one property's (its Assets tab)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Download(
            string? search, string? active, List<int>? assetType, bool assetTypeIsNot,
            List<string>? propertyStatus, int? site, bool f = false, CancellationToken ct = default)
        {
            var filter = Filter(search, active, assetType, assetTypeIsNot, propertyStatus, f);
            filter.SiteId = site;
            return File(await _assetList.CsvAsync(filter, ct), "text/csv", FyreApp.Infrastructure.CsvExport.FileName("assets"));
        }

        // Edit: set active / inactive, or generate a task per property, for the ticked assets or all matching
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> BulkEdit(
            string bulkAction, [Bind(Prefix = "task")] FyreApp.ViewModels.Lists.BulkTaskInput task,
            List<int>? ids, bool allMatching, string? returnUrl,
            string? search, string? active, List<int>? assetType, bool assetTypeIsNot,
            List<string>? propertyStatus, bool f = false, CancellationToken ct = default)
        {
            var selection = FyreApp.ViewModels.Lists.BulkSelection.From(ids, allMatching);
            var filter = Filter(search, active, assetType, assetTypeIsNot, propertyStatus, f);

            switch (bulkAction)
            {
                case "activate":
                case "deactivate":
                    var makeActive = bulkAction == "activate";
                    var changed = await _assetList.SetActiveAsync(selection, filter, makeActive, ct);
                    return FyreApp.Infrastructure.ListControllerExtensions.BackToList(this, returnUrl,
                        $"Set {FyreApp.Infrastructure.ListControllerExtensions.Plural(changed, "asset", "assets")} {(makeActive ? "active" : "inactive")}.");
                case "tasks":
                    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var created = await _assetList.GenerateTasksAsync(selection, filter, task, userId, ct);
                    return FyreApp.Infrastructure.ListControllerExtensions.BackToList(this, returnUrl,
                        $"Generated {FyreApp.Infrastructure.ListControllerExtensions.Plural(created, "task", "tasks")}, one per property.");
                default:
                    return FyreApp.Infrastructure.ListControllerExtensions.BackToList(this, returnUrl, "Nothing changed: choose what to do.");
            }
        }

        [HttpGet("/api/assets/suggestions")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Suggestions(string? q, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(Array.Empty<string>());

            var term = q.Trim();
            var pattern = $"%{term}%";

            var matches = await _context.AssetCatalogue
                .Where(a => EF.Functions.ILike(a.Name, pattern))
                .Select(a => a.Name)
                .Distinct()
                .OrderBy(a => a)
                .Take(20)
                .ToListAsync(ct);

            var results = matches
                .OrderBy(n => n.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n)
                .Take(8);

            return Json(results);
        }

        // GET: /Assets/Details/5?tab=defects — the asset page, laid out like Uptick's
        public async Task<IActionResult> Details(int id, string? tab, bool notes = false, CancellationToken ct = default)
        {
            var page = await _page.LoadAsync(id, tab, notes, ct);
            if (page == null) return NotFound();

            SetBreadcrumbs(page.Asset);
            return View(page);
        }

        // Uptick's asset Update form
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
        {
            var form = await _page.EditFormAsync(id, ct);
            if (form == null) return NotFound();

            SetBreadcrumbs(form.Asset!, update: true);
            return View(form);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, AssetEditVm form, bool continueEditing = false, CancellationToken ct = default)
        {
            form.Id = id;
            if (form.AssetTypeId == null)
                ModelState.AddModelError(nameof(form.AssetTypeId), "Choose the asset type.");

            if (!ModelState.IsValid)
            {
                await _page.FillEditFormAsync(form, ct);
                if (form.Asset == null) return NotFound();
                SetBreadcrumbs(form.Asset, update: true);
                return View(form);
            }

            if (!await _page.SaveAsync(form, await _context.AuthorNameAsync(User, ct), ct))
                return NotFound();

            TempData["Success"] = "Asset saved.";
            return continueEditing ? RedirectToAction(nameof(Edit), new { id }) : RedirectToAction(nameof(Details), new { id });
        }

        // "Add note" on the asset's Timeline
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddNote(int id, string? text, CancellationToken ct)
        {
            if (!await _context.Assets.AnyAsync(a => a.Id == id, ct)) return NotFound();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _context.TimelineEntries.Add(new TimelineEntry { AssetId = id, Author = await _context.AuthorNameAsync(User, ct), IsNote = true, Text = text.Trim() });
                await _context.SaveChangesAsync(ct);
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        // As Uptick: Home / Properties / P-3330 / Assets / 001 - DCP AB(E) 4.5KG Port… (/ Update)
        private void SetBreadcrumbs(Asset asset, bool update = false)
        {
            var label = asset.Name.Length > 28 ? asset.Name[..28] + "…" : asset.Name;
            var crumbs = new List<(string Text, string? Url)>
            {
                ("Home", Url.Action("Index", "Home")),
                ("Properties", Url.Action("Index", "Property")),
                (asset.Site.DisplayRef ?? asset.Site.Name, Url.Action("Details", "Property", new { id = asset.SiteId })),
                ("Assets", Url.Action("Details", "Property", new { id = asset.SiteId, tab = "assets" })),
                (label, update ? Url.Action(nameof(Details), new { id = asset.Id }) : null)
            };
            if (update) crumbs.Add(("Update", null));
            ViewData["Breadcrumbs"] = crumbs;
        }
    }
}
