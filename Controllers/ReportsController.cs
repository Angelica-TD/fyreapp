using FyreApp.Infrastructure;
using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Service reports, listed like Uptick's Reports page (no default filters, no Edit)
public class ReportsController : Controller
{
    private readonly IReportListService _list;
    public ReportsController(IReportListService list) => _list = list;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default) =>
        View(await _list.SearchAsync(new ReportFilter { Search = search, Page = page }, ct));

    // Every report matching the search, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(string? search, CancellationToken ct = default) =>
        File(await _list.CsvAsync(new ReportFilter { Search = search }, ct), "text/csv", CsvExport.FileName("reports"));
}
