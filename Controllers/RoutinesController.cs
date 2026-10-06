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
    private readonly IRoutineService _routines;
    public RoutinesController(IRoutineService routines) => _routines = routines;

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineOccurrenceStatus>? status, int page = 1, bool f = false,
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
        List<string>? propertyStatus, List<RoutineOccurrenceStatus>? status, bool f = false,
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
        List<string>? propertyStatus, List<RoutineOccurrenceStatus>? status, bool f = false,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(search, clientActive, dueFrom, dueTo, propertyStatus, status, f);
        var result = await _routines.GenerateTasksAsync(ids ?? new(), allMatching, filter, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);

        TempData["Success"] = result.TasksCreated == 0
            ? $"No tasks generated: none of the selected routines are pending.{Skipped(result)}"
            : $"Generated {result.TasksCreated} task{(result.TasksCreated == 1 ? "" : "s")} from {result.RoutinesUsed} routine{(result.RoutinesUsed == 1 ? "" : "s")}.{Skipped(result)}";

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));

        static string Skipped(GenerateRoutineTasksResult r) =>
            r.RoutinesSkipped == 0 ? "" : $" {r.RoutinesSkipped} skipped (task already raised, complete or cancelled).";
    }

    // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
    // submitted, what's in the query string is used as is, so a cleared filter means "any".
    private static RoutineFilter BuildFilter(
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineOccurrenceStatus>? status, bool f)
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
