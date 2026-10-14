using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using Microsoft.AspNetCore.Authorization;

using FyreApp.Services.Sites;
using FyreApp.ViewModels.Sites;

namespace FyreApp.Controllers
{
    public class PropertyController : Controller
    {
        private readonly AppDbContext _context;
        private readonly SitesService _sites;
        private readonly FyreApp.Services.Lists.IPropertyListService _list;
        private readonly FyreApp.Services.Sites.IPropertyPageService _page;

        public PropertyController(AppDbContext context, SitesService sites, FyreApp.Services.Lists.IPropertyListService list,
            FyreApp.Services.Sites.IPropertyPageService page)
        {
            _context = context;
            _sites = sites;
            _list = list;
            _page = page;
        }

        // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
        // submitted, what's in the query string is used as is, so a cleared filter means "any".
        private static FyreApp.ViewModels.Lists.PropertyFilter Filter(string? search, List<string>? status, bool f)
        {
            var filter = new FyreApp.ViewModels.Lists.PropertyFilter { Search = search };
            if (f) filter.Status = status ?? new();
            return filter;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search, List<string>? status, int page = 1, bool f = false, CancellationToken ct = default)
        {
            var filter = Filter(search, status, f);
            filter.Page = page;
            return View(await _list.SearchAsync(filter, ct));
        }

        // Every property matching the current filters, as CSV
        [HttpGet]
        public async Task<IActionResult> Download(string? search, List<string>? status, bool f = false, CancellationToken ct = default) =>
            File(await _list.CsvAsync(Filter(search, status, f), ct), "text/csv", FyreApp.Infrastructure.CsvExport.FileName("properties"));

        // Edit: change status, or generate a task per property, for the ticked properties or all matching the filters
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BulkEdit(
            string bulkAction, string? newStatus, [Bind(Prefix = "task")] FyreApp.ViewModels.Lists.BulkTaskInput task,
            List<int>? ids, bool allMatching, string? returnUrl,
            string? search, List<string>? status, bool f = false, CancellationToken ct = default)
        {
            var selection = FyreApp.ViewModels.Lists.BulkSelection.From(ids, allMatching);
            var filter = Filter(search, status, f);
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            switch (bulkAction)
            {
                case "status" when !string.IsNullOrWhiteSpace(newStatus):
                    var changed = await _list.SetStatusAsync(selection, filter, newStatus, ct);
                    return this.BackToList(returnUrl, $"Set {FyreApp.Infrastructure.ListControllerExtensions.Plural(changed, "property", "properties")} to {FyreApp.ViewModels.Lists.ListFilters.PropertyStatusLabel(newStatus)}.");
                case "tasks":
                    var created = await _list.GenerateTasksAsync(selection, filter, task, userId, ct);
                    return this.BackToList(returnUrl, $"Generated {FyreApp.Infrastructure.ListControllerExtensions.Plural(created, "task", "tasks")}, one per property.");
                default:
                    return this.BackToList(returnUrl, "Nothing changed: choose what to do.");
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(CreateSiteRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please fix the errors and try again.";
                return RedirectToAction("Details", "Clients", new { id = request.ClientId });
            }

            var result = await _sites.CreateAsync(request, ct);
            await LogCreatedAsync(result, ct);

            return result.Status switch
            {
                CreateSiteStatus.Success => RedirectToAction("Details", "Clients", new { id = request.ClientId }),
                CreateSiteStatus.NotFound => NotFound(),
                CreateSiteStatus.GeocodeFailed => RedirectWithError(request.ClientId, result.Error ?? "Address lookup failed."),
                CreateSiteStatus.DuplicateName => Conflict(result.Error ?? "A property with this name already exists for this client."),
                CreateSiteStatus.DuplicateAddress => Conflict(result.Error ?? "A property with this address already exists for this client."),
                _ => RedirectWithError(request.ClientId, result.Error ?? "Could not create property.")
            };

            
        }

        // A property made in FyreApp starts its Timeline, as in Uptick
        private async Task LogCreatedAsync(CreateSiteResult result, CancellationToken ct)
        {
            if (result.Status != CreateSiteStatus.Success || result.site == null) return;
            _context.TimelineEntries.Add(new TimelineEntry { SiteId = result.site.Id, Author = await _context.AuthorNameAsync(User, ct), Text = "Property was created." });
            await _context.SaveChangesAsync(ct);
        }

        // "Add note" on the property's Timeline
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddNote(int id, string? text, CancellationToken ct)
        {
            if (!await _context.Sites.AnyAsync(s => s.Id == id, ct)) return NotFound();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _context.TimelineEntries.Add(new TimelineEntry { SiteId = id, Author = await _context.AuthorNameAsync(User, ct), IsNote = true, Text = text.Trim() });
                await _context.SaveChangesAsync(ct);
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        private IActionResult RedirectWithError(int clientId, string message)
        {
            TempData["Error"] = message;
            return RedirectToAction("Details", "Clients", new { id = clientId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAjax(CreateSiteRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _sites.CreateAsync(request, ct);
            await LogCreatedAsync(result, ct);

            return result.Status switch
            {
                CreateSiteStatus.Success => Json(new
                {
                    id = result.site?.Id,
                    name = result.SiteName 
                }),

                CreateSiteStatus.NotFound => NotFound(),

                CreateSiteStatus.GeocodeFailed => BadRequest(result.Error ?? "Address lookup failed."),

                CreateSiteStatus.DuplicateName => Conflict(result.Error ?? "A property with this name already exists for this client."),
                CreateSiteStatus.DuplicateAddress => Conflict(result.Error ?? "A property with this address already exists for this client."),

                _ => BadRequest(result.Error ?? "Could not create property.")
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAsset(int siteId, string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Asset name is required.";
                return RedirectToAction("Details", new { id = siteId, tab = "assets" });
            }

            var site = await _context.Sites.FindAsync(new object[] { siteId }, ct);
            if (site == null)
                return NotFound();

            var asset = new Asset
            {
                SiteId = siteId,
                Name = name
            };

            _context.Assets.Add(asset);
            await _context.SaveChangesAsync(ct);
            _context.TimelineEntries.Add(new TimelineEntry { AssetId = asset.Id, Author = await _context.AuthorNameAsync(User, ct), Text = "Asset was created." });
            await _context.SaveChangesAsync(ct);

            return RedirectToAction("Details", new { id = siteId, tab = "assets" });
        }

        // GET: /Property/Details/5?tab=assets — the property page, one Uptick-style tab at a time
        public async Task<IActionResult> Details(
            int id, string? tab, int? year, string? active, int? assetType, string? search, bool closed = false, bool notes = false,
            CancellationToken ct = default)
        {
            var page = await _page.LoadAsync(id, new FyreApp.Services.Sites.PropertyPageQuery(tab, year, active, assetType, search, closed, notes), ct);
            if (page == null)
                return NotFound();

            // As Uptick: Home / Properties / P-0391
            ViewData["Breadcrumbs"] = new List<(string Text, string? Url)>
            {
                ("Home", Url.Action("Index", "Home")),
                ("Properties", Url.Action(nameof(Index))),
                (page.Site.DisplayRef ?? page.Site.Name, null)
            };

            return View(page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var site = await _context.Sites.FindAsync(id);
            if (site == null) return NotFound();

            site.Active = false;
            _context.TimelineEntries.Add(new TimelineEntry { SiteId = id, Author = await _context.AuthorNameAsync(User), Field = "Status", Value = "Inactive" });
            await _context.SaveChangesAsync();

            TempData["Success"] = "Property deactivated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Activate(int id)
        {
            var site = await _context.Sites.FindAsync(id);
            if (site == null) return NotFound();

            site.Active = true;
            _context.TimelineEntries.Add(new TimelineEntry { SiteId = id, Author = await _context.AuthorNameAsync(User), Field = "Status", Value = "Active" });
            await _context.SaveChangesAsync();

            TempData["Success"] = "Property activated.";
            return RedirectToAction(nameof(Details), new { id });
        }

    }
}
