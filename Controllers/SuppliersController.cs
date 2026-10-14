using FyreApp.Infrastructure;
using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Suppliers imported from Uptick: search and Active (default active)
[Authorize(Roles = "Admin")]
public class SuppliersController : Controller
{
    private readonly IDirectoryListService _list;
    public SuppliersController(IDirectoryListService list) => _list = list;

    private static DirectoryFilter Filter(string? search, string? active, int page = 1) =>
        new() { Search = search, Active = DirectoryFilter.NormaliseActive(active), Page = page };

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? active, int page = 1, CancellationToken ct = default) =>
        View(await _list.SuppliersAsync(Filter(search, active, page), ct));

    // Every supplier matching the filters, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(string? search, string? active, CancellationToken ct = default) =>
        File(await _list.SuppliersCsvAsync(Filter(search, active), ct), "text/csv", CsvExport.FileName("suppliers"));
}
