using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Authorization;

namespace FyreApp.Controllers
{
    public class PropertyController : Controller
    {
        private readonly AppDbContext _context;

        public PropertyController(AppDbContext context)
        {
            _context = context;
        }
        

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int clientId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Site name is required.");

            var clientExists = await _context.Clients.AnyAsync(c => c.Id == clientId);
            if (!clientExists)
                return NotFound();

            var site = new Site
            {
                Name = name.Trim(),
                ClientId = clientId
            };

            _context.Sites.Add(site);
            await _context.SaveChangesAsync();

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
