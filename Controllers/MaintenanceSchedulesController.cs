using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;

namespace FyreApp.Controllers
{
    public class MaintenanceSchedulesController : Controller
    {
        private readonly AppDbContext _context;

        public MaintenanceSchedulesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: MaintenanceSchedulesController
        public ActionResult Index()
        {
            return View();
        }

        // Create schedule for SITE
        [HttpPost]
        public async Task<IActionResult> CreateForSite(int siteId, DateTime startDate, int intervalId)
        {
            var interval = await _context.MaintenanceIntervals.FindAsync(intervalId);
            
            if (interval == null) return BadRequest();

            var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

            var schedule = new MaintenanceSchedule
            {
                TargetType = ScheduleTargetType.Site,
                SiteId = siteId,
                StartDate = startUtc,
                NextRunDate = startUtc.AddMonths(interval.Months)
            };

            _context.MaintenanceSchedules.Add(schedule);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Sites", new { id = siteId });
        }

        // Create schedule for ASSET
        [HttpPost]
        public async Task<IActionResult> CreateForAsset(int assetId, DateTime startDate, int intervalId)
        {
            var interval = await _context.MaintenanceIntervals.FindAsync(intervalId);
            
            if (interval == null) return BadRequest();

            var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var schedule = new MaintenanceSchedule
            {
                TargetType = ScheduleTargetType.Asset,
                AssetId = assetId,
                StartDate = startUtc,
                NextRunDate = startUtc.AddMonths(interval.Months)
            };

            _context.MaintenanceSchedules.Add(schedule);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Assets", new { id = assetId });
        }


        [HttpPost]
        public async Task<IActionResult> Create(
            ScheduleTargetType targetType,
            int targetId,
            DateTime startDate,
            int intervalId)
        {
            var interval = await _context.MaintenanceIntervals.FindAsync(intervalId);
            if (interval == null)
                return BadRequest("Invalid interval");

            // PostgreSQL requires UTC
            var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

            var schedule = new MaintenanceSchedule
            {
                TargetType = targetType,
                MaintenanceIntervalId = interval.Id,
                StartDate = startUtc,
                NextRunDate = startUtc.AddMonths(interval.Months)
            };

            if (targetType == ScheduleTargetType.Site)
            {
                schedule.SiteId = targetId;
            }
            else if (targetType == ScheduleTargetType.Asset)
            {
                schedule.AssetId = targetId;
            }
            else
            {
                return BadRequest("Invalid target type");
            }

            _context.MaintenanceSchedules.Add(schedule);
            await _context.SaveChangesAsync();

            return targetType == ScheduleTargetType.Site
                ? RedirectToAction("Details", "Sites", new { id = targetId })
                : RedirectToAction("Details", "Assets", new { id = targetId });
        }

    }
}
