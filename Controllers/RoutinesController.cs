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

    // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
    // submitted, what's in the query string is used as is, so a cleared filter means "any".
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, string? clientActive, DateTime? dueFrom, DateTime? dueTo,
        List<string>? propertyStatus, List<RoutineOccurrenceStatus>? status, int page = 1, bool f = false,
        CancellationToken ct = default)
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
        filter.Page = page;

        return View(await _routines.SearchAsync(filter, ct));
    }
}
