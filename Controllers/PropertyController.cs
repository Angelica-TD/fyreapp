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
