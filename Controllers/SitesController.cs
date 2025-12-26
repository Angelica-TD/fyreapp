using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;

namespace FyreApp.Controllers
{
    public class SitesController : Controller
    {
        private readonly AppDbContext _context;

        public SitesController(AppDbContext context)
        {
            _context = context;
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
