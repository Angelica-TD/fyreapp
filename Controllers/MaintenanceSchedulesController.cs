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
        
        [HttpGet]
        public async Task<IActionResult> Upsert(ScheduleTargetType targetType, int targetId)
        {
            MaintenanceSchedule schedule = null;

            if (targetType == ScheduleTargetType.Site)
            {
                schedule = await _context.MaintenanceSchedules
                    .Include(s => s.MaintenanceInterval)
                    .FirstOrDefaultAsync(s => s.TargetType == targetType && s.SiteId == targetId);
            }
            else if (targetType == ScheduleTargetType.Asset)
            {
                schedule = await _context.MaintenanceSchedules
                    .Include(s => s.MaintenanceInterval)
                    .FirstOrDefaultAsync(s => s.TargetType == targetType && s.AssetId == targetId);
            }

            // If no schedule exists, create a new one (for form binding)
            if (schedule == null)
            {
                schedule = new MaintenanceSchedule
                {
                    TargetType = targetType,
                    SiteId = targetType == ScheduleTargetType.Site ? targetId : null,
                    AssetId = targetType == ScheduleTargetType.Asset ? targetId : null,
                    StartDate = DateTime.UtcNow
                };
            }

            ViewData["Intervals"] = await _context.MaintenanceIntervals.ToListAsync();
            return View(schedule);

        }

        
        [HttpPost]
        public async Task<IActionResult> Upsert(int scheduleId, ScheduleTargetType targetType, int targetId, DateTime startDate, int intervalId)
        {
            var interval = await _context.MaintenanceIntervals.FindAsync(intervalId);
            if (interval == null) return BadRequest("Invalid interval");

            MaintenanceSchedule schedule;

            if (scheduleId > 0)
            {
                schedule = await _context.MaintenanceSchedules.FindAsync(scheduleId);
                if (schedule == null) return NotFound();
            }
            else
            {
                schedule = new MaintenanceSchedule
                {
                    TargetType = targetType,
                    SiteId = targetType == ScheduleTargetType.Site ? targetId : null,
                    AssetId = targetType == ScheduleTargetType.Asset ? targetId : null
                };
                _context.MaintenanceSchedules.Add(schedule);
            }

            schedule.StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            schedule.MaintenanceIntervalId = interval.Id;
            schedule.NextRunDate = schedule.StartDate.AddMonths(interval.Months);

            await _context.SaveChangesAsync();

            if (targetType == ScheduleTargetType.Site)
                return RedirectToAction("Details", "Sites", new { id = targetId });
            else
                return RedirectToAction("Details", "Assets", new { id = targetId });
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id, string? notes)
        {
            var schedule = await _context.MaintenanceSchedules
                .Include(ms => ms.MaintenanceInterval)
                .FirstOrDefaultAsync(ms => ms.Id == id);

            if (schedule == null) return NotFound();
            if (!schedule.IsActive) return BadRequest();

            var now = DateTime.UtcNow;

            var history = new MaintenanceHistory
            {
                MaintenanceScheduleId = schedule.Id,
                CompletedAt = now,
                DueDateAtCompletion = schedule.NextRunDate,
                Notes = notes
            };

            _context.MaintenanceHistory.Add(history);

            var months = schedule.MaintenanceInterval?.Months ?? 0;
            if (months <= 0) return BadRequest("Invalid interval.");

            // Advance from due date (prevents drift)
            schedule.NextRunDate = schedule.NextRunDate.Date.AddMonths(months);

            await _context.SaveChangesAsync();
            return RedirectToAction("Details", new { id = schedule.Id });
        }


    }
}
