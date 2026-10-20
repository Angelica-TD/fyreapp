using FyreApp.Services.Lists;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Uptick's emails (correspondence): the list, optionally those about one record, and an email's page
[Authorize(Roles = "Admin")]
public class CorrespondenceController : Controller
{
    private readonly ICorrespondenceListService _list;
    public CorrespondenceController(ICorrespondenceListService list) => _list = list;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? record, long? recordId, int page = 1, CancellationToken ct = default) =>
        View(await _list.SearchAsync(new CorrespondenceFilter { Search = search, Record = record, RecordId = recordId, Page = page }, ct));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct = default) =>
        await _list.DetailsAsync(id, ct) is { } vm ? View(vm) : NotFound();
}
