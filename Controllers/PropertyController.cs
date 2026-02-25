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
        public async Task<IActionResult> CreateAjax(int clientId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Site name is required.");

            var trimmed = name.Trim();

            var clientExists = await _context.Clients.AnyAsync(c => c.Id == clientId);
            if (!clientExists)
                return NotFound();

            // prevent duplicate site names per client
            var duplicate = await _context.Sites.AnyAsync(s =>
                s.ClientId == clientId && s.Name.ToLower() == trimmed.ToLower());

            if (duplicate)
                return Conflict("A property with this name already exists for this client.");

            var site = new Site
            {
                Name = trimmed,
                ClientId = clientId
            };

            _context.Sites.Add(site);
            await _context.SaveChangesAsync();

            return Json(new { id = site.Id, name = site.Name });
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
