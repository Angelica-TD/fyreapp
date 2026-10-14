using FyreApp.Infrastructure;
using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Contractors imported from Uptick: search and Active (default active)
[Authorize(Roles = "Admin")]
public class ContractorsController : Controller
{
    private readonly IDirectoryListService _list;
    public ContractorsController(IDirectoryListService list) => _list = list;

    private static DirectoryFilter Filter(string? search, string? active, int page = 1) =>
        new() { Search = search, Active = DirectoryFilter.NormaliseActive(active), Page = page };

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? active, int page = 1, CancellationToken ct = default) =>
        View(await _list.ContractorsAsync(Filter(search, active, page), ct));

    // Every contractor matching the filters, as CSV
    [HttpGet]
    public async Task<IActionResult> Download(string? search, string? active, CancellationToken ct = default) =>
        File(await _list.ContractorsCsvAsync(Filter(search, active), ct), "text/csv", CsvExport.FileName("contractors"));
}
