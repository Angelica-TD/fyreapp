using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Uptick "Remarks" = FyreApp defects, listed like Uptick's Remarks page
public class RemarksController : Controller
{
    private readonly IListService _lists;
    public RemarksController(IListService lists) => _lists = lists;

    // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
    // submitted, what's in the query string is used as is, so a cleared filter means "any".
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, string? assetActive, List<RemarkCompliance>? compliance, List<string>? propertyStatus,
        List<string>? severity, int page = 1, bool f = false, CancellationToken ct = default)
    {
        var filter = new RemarkFilter { Search = search, Page = page };
        if (f)
        {
            filter.AssetActive = assetActive switch { "yes" => true, "no" => false, _ => null };
            filter.Compliance = compliance ?? new();
            filter.PropertyStatus = propertyStatus ?? new();
            filter.Severity = severity ?? new();
        }

        return View(await _lists.RemarksAsync(filter, ct));
    }
}
