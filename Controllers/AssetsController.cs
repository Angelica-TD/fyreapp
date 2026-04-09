using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;

namespace FyreApp.Controllers
{
    public class AssetsController : Controller
    {
        private readonly AppDbContext _context;
        public AssetsController(AppDbContext context) => _context = context;

        [HttpGet("/api/assets/suggestions")]
        public async Task<IActionResult> Suggestions(string? q, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(Array.Empty<string>());

            var term = q.Trim();
            var pattern = $"%{term}%";

            var matches = await _context.AssetCatalogue
                .Where(a => EF.Functions.ILike(a.Name, pattern))
                .OrderBy(a => a.Name)
                .Take(20)
                .Select(a => a.Name)
                .ToListAsync(ct);

            var results = matches
                .OrderBy(n => n.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n)
                .Take(8);

            return Json(results);
        }

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
