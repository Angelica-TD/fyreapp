using FyreApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.ViewModels.MaintenanceSchedules;

namespace FyreApp.Controllers
{
    public class MaintenanceSchedulesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMaintenanceScheduleService _scheduleService;
        private readonly IScheduleImportService _importService;
        private readonly UserManager<ApplicationUser> _userManager;

        public MaintenanceSchedulesController(
            AppDbContext context,
            IMaintenanceScheduleService scheduleService,
            IScheduleImportService importService,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _scheduleService = scheduleService;
            _importService = importService;
            _userManager = userManager;
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Import() => View();

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ImportLimits.MaxUploadBytes)]
        public async Task<IActionResult> Import(IFormFile? file, bool dryRun, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("file", "Choose a CSV or XLSX file to import.");
                return View();
            }

            await using var stream = file.OpenReadStream();
            var result = await _importService.ImportUptickAsync(stream, file.FileName, dryRun, ct);

            return View(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Index(string? window, string? targetType, string? status)
        {
            var filter = new MaintenanceScheduleFilter
            {
                Window = Enum.TryParse<ScheduleWindow>(window, true, out var w) ? w : ScheduleWindow.Month,
                TargetType = Enum.TryParse<ScheduleTargetType>(targetType, true, out var t) ? t : null,
                GenerationStatus = Enum.TryParse<ScheduleGenerationStatus>(status, true, out var s) ? s : ScheduleGenerationStatus.Pending
            };

            var schedules = await _scheduleService.GetDueListAsync(filter);

            return View(new MaintenanceScheduleIndexVm
            {
                Schedules = schedules,
                Window = filter.Window,
                TargetType = filter.TargetType,
                GenerationStatus = filter.GenerationStatus
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var schedule = await _scheduleService.GetDetailsAsync(id);
            if (schedule == null) return NotFound();

            ViewData["Intervals"] = await _context.MaintenanceIntervals.ToListAsync();
            return View(schedule);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateTask(int id, string? window, string? targetType, string? status)
        {
            var exists = await _context.MaintenanceSchedules.AnyAsync(s => s.Id == id);
            if (!exists) return NotFound();

            await _scheduleService.GenerateTaskAsync(id, _userManager.GetUserId(User)!);

            TempData["Success"] = "Task generated.";
            return RedirectToAction(nameof(Index), new { window, targetType, status });
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
                return RedirectToAction("Details", "Property", new { id = targetId });
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
                ? RedirectToAction("Details", "Property", new { id = targetId })
                : RedirectToAction("Details", "Assets", new { id = targetId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id, string? notes)
        {
            var status = await _scheduleService.CompleteAsync(id, notes);

            return status switch
            {
                ScheduleCompleteStatus.Success => RedirectToAction(nameof(Details), new { id }),
                ScheduleCompleteStatus.NotFound => NotFound(),
                _ => BadRequest()
            };
        }


    }
}
