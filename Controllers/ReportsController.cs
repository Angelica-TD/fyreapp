using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Service reports, listed like Uptick's Reports page (no default filters)
public class ReportsController : Controller
{
    private readonly IListService _lists;
    public ReportsController(IListService lists) => _lists = lists;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default) =>
        View(await _lists.ReportsAsync(new ReportFilter { Search = search, Page = page }, ct));
}
