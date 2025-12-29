using FyreApp.ViewModels.Home;
using FyreApp.Models;
using FyreApp.Data;
using FyreApp.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace FyreApp.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    [HttpGet]
    public IActionResult Index(string? returnUrl = null)
    {
        var vm = new HomeIndexVm();
        vm.Login.ReturnUrl = returnUrl ?? Url.Action(nameof(Index), "Home");
        return View(vm);
    }
    

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MaintenanceEvents(DateTime start, DateTime end)
    {
        // Normalie to date boundaries
        var rangeStart = start.Date;
        var rangeEnd = end.Date;

        var schedules = await _db.MaintenanceSchedules
            .AsNoTracking()
            .Where(ms => ms.IsActive)
            .Where(ms => ms.NextRunDate >= rangeStart && ms.NextRunDate < rangeEnd)
            .Include(ms => ms.MaintenanceInterval) // interval name/months
            .Include(ms => ms.Site)                // for Site-targeted schedules
                .ThenInclude(s => s!.Client)
            .Include(ms => ms.Asset)               // for Asset-targeted schedules
                .ThenInclude(a => a!.Site)
                    .ThenInclude(s => s!.Client)
            .ToListAsync();

        var today = DateTime.Today;

        var events = schedules.Select(ms =>
        {
            var (clientName, siteName, assetName) = ms.TargetType switch
            {
                ScheduleTargetType.Site => (
                    ms.Site?.Client?.Name ?? "Client",
                    ms.Site?.Name ?? "Site",
                    (string?)null
                ),
                ScheduleTargetType.Asset => (
                    ms.Asset?.Site?.Client?.Name ?? "Client",
                    ms.Asset?.Site?.Name ?? "Site",
                    ms.Asset?.Name
                ),
                _ => ("Client", "Site", (string?)null)
            };

            var isOverdue = ms.NextRunDate.Date < today;

            var title = ms.TargetType == ScheduleTargetType.Asset && assetName != null
                ? $"{clientName} • {siteName} • {assetName}"
                : $"{clientName} • {siteName}";

            return new CalendarEventDto
            {
                Title = title,
                Start = ms.NextRunDate.ToString("yyyy-MM-dd"),
                ClassName = isOverdue ? "overdue" : "due",
                // link to wherever you manage schedules
                Url = Url.Action("Details", "MaintenanceSchedules", new { id = ms.Id })
            };
        });

        return Json(events);
    }

    
}
