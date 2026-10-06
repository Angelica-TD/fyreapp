using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Services.Assets;
using FyreApp.ViewModels.Assets;

namespace FyreApp.Controllers
{
    public class AssetsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IAssetListService _assetList;

        public AssetsController(AppDbContext context, IAssetListService assetList)
        {
            _context = context;
            _assetList = assetList;
        }

        // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
        // submitted, what's in the query string is used as is, so a cleared filter means "any".
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search, string? active, List<int>? assetType, bool assetTypeIsNot,
            List<string>? propertyStatus, int page = 1, bool f = false, CancellationToken ct = default)
        {
            var filter = new AssetFilter();
            if (f)
            {
                filter.Active = active switch { "yes" => true, "no" => false, _ => null };
                filter.AssetTypeIds = assetType ?? new();
                filter.AssetTypeIsNot = assetTypeIsNot;
                filter.PropertyStatus = propertyStatus ?? new();
            }
            filter.Search = search;
            filter.Page = page;

            return View(await _assetList.SearchAsync(filter, ct));
        }

        [HttpGet("/api/assets/suggestions")]
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
                .Include(a => a.AssetTypeVariant)
                .Include(a => a.MaintenanceSchedules)
                    .ThenInclude(ms => ms.MaintenanceHistory)
                .Include(a => a.MaintenanceSchedules)
                    .ThenInclude(ms => ms.GeneratedTasks)
                .Include(a => a.Defects)
                .AsSplitQuery()
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null) return NotFound();

            ViewBag.Intervals = await _context.MaintenanceIntervals.ToListAsync();

            return View(asset);
        }

    }
}
