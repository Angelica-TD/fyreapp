using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
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

        public PropertyController(AppDbContext context, SitesService sites)
        {
            _context = context;
            _sites = sites;
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
                return RedirectToAction("Details", new { id = siteId });
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

            return RedirectToAction("Details", new { id = siteId });
        }

        // GET: /Sites/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var site = await _context.Sites
                .Include(s => s.Client)
                .Include(s => s.Assets)
                    .ThenInclude(a => a.AssetTypes)
                .Include(s => s.MaintenanceSchedules)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (site == null)
                return NotFound();

            ViewBag.Intervals = await _context.MaintenanceIntervals.ToListAsync();

            return View(site);
        }

    }
}
