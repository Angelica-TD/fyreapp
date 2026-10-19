using System.Security.Claims;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Routines;
using FyreApp.ViewModels.Routines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class RoutinesController : Controller
{
    private readonly IRoutineListService _routines;
    public RoutinesController(IRoutineListService routines) => _routines = routines;

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineStatus>? status, int page = 1, bool f = false,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(search, clientActive, dueFrom, dueTo, propertyStatus, status, f);
        filter.Page = page;
        return View(await _routines.SearchAsync(filter, ct));
    }

    // Every routine matching the current filters, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineStatus>? status, bool f = false,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(search, clientActive, dueFrom, dueTo, propertyStatus, status, f);
        var csv = await _routines.DownloadCsvAsync(filter, ct);
        return File(csv, "text/csv", $"routines_{DateTime.UtcNow.ToSydney():yyyy-MM-dd_HH-mm}.csv");
    }

    // Edit routines → Generate tasks: the ticked routines (ids), or every routine matching the filters (allMatching)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateTasks(
        List<int>? ids, bool allMatching, string? returnUrl,
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineStatus>? status, bool f = false,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(search, clientActive, dueFrom, dueTo, propertyStatus, status, f);
        var result = await _routines.GenerateTasksAsync(ids ?? new(), allMatching, filter, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);

        TempData["Success"] = result.TasksCreated == 0
            ? $"No tasks generated: none of the selected routines are pending.{Skipped(result)}"
            : $"Generated {result.TasksCreated} task{(result.TasksCreated == 1 ? "" : "s")} from {result.RoutinesUsed} routine{(result.RoutinesUsed == 1 ? "" : "s")}.{Skipped(result)}";

        if (result.TasksCreated > 0)
            TempData["TasksLink"] = TasksToAssignUrl(filter);

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));

        static string Skipped(GenerateRoutineTasksResult r) =>
            r.RoutinesSkipped == 0 ? "" : $" {r.RoutinesSkipped} skipped (task already raised, complete or cancelled).";
    }

    // The Tasks list showing the routine tasks just generated that still need a technician. Generated tasks are
    // due at the end of their routines' month, so the due filter covers whole months.
    private string TasksToAssignUrl(RoutineFilter filter)
    {
        var query = new List<string> { "category=I%26T", "technician=none" };
        if (filter.DueFrom is DateTime from)
            query.Add($"dueFrom={new DateTime(from.Year, from.Month, 1):yyyy-MM-dd}");
        if (filter.DueTo is DateTime to)
            query.Add($"dueTo={new DateTime(to.Year, to.Month, DateTime.DaysInMonth(to.Year, to.Month)):yyyy-MM-dd}");
        return $"{Url.Action("Index", "Task")}?{string.Join("&", query)}";
    }

    // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
    // submitted, what's in the query string is used as is, so a cleared filter means "any".
    private static RoutineFilter BuildFilter(
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineStatus>? status, bool f)
    {
        var filter = RoutineFilter.Default(DateTime.UtcNow.ToSydney());
        if (f)
        {
            filter.ClientActive = clientActive switch { "yes" => true, "no" => false, _ => null };
            filter.DueFrom = dueFrom;
            filter.DueTo = dueTo;
            filter.PropertyStatus = propertyStatus ?? new();
            filter.Status = status ?? new();
        }
        filter.Search = search;
        return filter;
    }
}
