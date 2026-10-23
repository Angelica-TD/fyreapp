using FyreApp.Infrastructure;
using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Service reports, listed like Uptick's Reports page (no default filters, no Edit)
[Authorize(Roles = "Admin")]
public class ReportsController : Controller
{
    private readonly IReportListService _list;
    public ReportsController(IReportListService list) => _list = list;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default) =>
        View(await _list.SearchAsync(new ReportFilter { Search = search, Page = page }, ct));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct = default) =>
        await _list.DetailsAsync(id, ct) is { } vm ? View(vm) : NotFound();

    // A report made in FyreApp as the customer gets it, to print or save as a PDF (Uptick's have their own PDF)
    [HttpGet]
    public async Task<IActionResult> Print(int id, CancellationToken ct = default) =>
        await _list.DetailsAsync(id, ct) is { Work: not null } vm ? View(vm) : NotFound();

    // Every report matching the search, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(string? search, CancellationToken ct = default) =>
        File(await _list.CsvAsync(new ReportFilter { Search = search }, ct), "text/csv", CsvExport.FileName("reports"));
}
