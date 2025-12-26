using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;

namespace FyreApp.Controllers
{
    public class AssetsController : Controller
    {
        private readonly AppDbContext _context;
        public AssetsController(AppDbContext context) => _context = context;
        // GET: AssetsController
        public ActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Details(int id)
        {
            var asset = await _context.Assets
                .Include(a => a.Site)
                    .ThenInclude(s => s.Client)
                .Include(a => a.AssetTypes)
                .Include(a => a.MaintenanceSchedules)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null) return NotFound();

            ViewBag.Intervals = await _context.MaintenanceIntervals.ToListAsync();

            return View(asset);
        }

    }
}
