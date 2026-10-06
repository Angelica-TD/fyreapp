using System.Security.Claims;
using FyreApp.Infrastructure;
using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Uptick "Remarks" = FyreApp defects, listed like Uptick's Remarks page
public class RemarksController : Controller
{
    private readonly IRemarkListService _list;
    public RemarksController(IRemarkListService list) => _list = list;

    // Without "f" (first visit, sidebar link) the Uptick-style defaults apply; once the filter form has been
    // submitted, what's in the query string is used as is, so a cleared filter means "any".
    private static RemarkFilter Filter(
        string? search, string? assetActive, List<RemarkCompliance>? compliance, List<string>? propertyStatus, List<string>? severity, bool f)
    {
        var filter = new RemarkFilter { Search = search };
        if (f)
        {
            filter.AssetActive = assetActive switch { "yes" => true, "no" => false, _ => null };
            filter.Compliance = compliance ?? new();
            filter.PropertyStatus = propertyStatus ?? new();
            filter.Severity = severity ?? new();
        }
        return filter;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, string? assetActive, List<RemarkCompliance>? compliance, List<string>? propertyStatus,
        List<string>? severity, int page = 1, bool f = false, CancellationToken ct = default)
    {
        var filter = Filter(search, assetActive, compliance, propertyStatus, severity, f);
        filter.Page = page;
        return View(await _list.SearchAsync(filter, ct));
    }

    // Every remark matching the current filters, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(
        string? search, string? assetActive, List<RemarkCompliance>? compliance, List<string>? propertyStatus,
        List<string>? severity, bool f = false, CancellationToken ct = default) =>
        File(await _list.CsvAsync(Filter(search, assetActive, compliance, propertyStatus, severity, f), ct),
            "text/csv", CsvExport.FileName("remarks"));

    // Edit: mark resolved / reopen, or generate a repair task per property, for the ticked remarks or all matching
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkEdit(
        string bulkAction, [Bind(Prefix = "task")] BulkTaskInput task, List<int>? ids, bool allMatching, string? returnUrl,
        string? search, string? assetActive, List<RemarkCompliance>? compliance, List<string>? propertyStatus,
        List<string>? severity, bool f = false, CancellationToken ct = default)
    {
        var selection = BulkSelection.From(ids, allMatching);
        var filter = Filter(search, assetActive, compliance, propertyStatus, severity, f);

        switch (bulkAction)
        {
            case "resolve":
            case "reopen":
                var resolved = bulkAction == "resolve";
                var changed = await _list.SetResolvedAsync(selection, filter, resolved, ct);
                return this.BackToList(returnUrl, $"{(resolved ? "Resolved" : "Reopened")} {ListControllerExtensions.Plural(changed, "remark", "remarks")}.");
            case "tasks":
                var created = await _list.GenerateRepairTasksAsync(selection, filter, task, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                return this.BackToList(returnUrl, $"Generated {ListControllerExtensions.Plural(created, "repair task", "repair tasks")}, one per property.");
            default:
                return this.BackToList(returnUrl, "Nothing changed: choose what to do.");
        }
    }
}
