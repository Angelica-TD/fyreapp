using FyreApp.Services.DataReset;
using FyreApp.Services.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class ImportsController : Controller
{
    private readonly IUptickImportService _importService;
    private readonly IDataResetService _resetService;

    public ImportsController(IUptickImportService importService, IDataResetService resetService)
    {
        _importService = importService;
        _resetService = resetService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(UptickExportType? type, CancellationToken ct)
    {
        ViewData["ResetEnabled"] = await _resetService.IsEnabledAsync(ct);
        ViewData["SelectedType"] = type;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Index(IFormFile? file, UptickExportType? type, bool dryRun, CancellationToken ct)
    {
        ViewData["ResetEnabled"] = await _resetService.IsEnabledAsync(ct);
        ViewData["SelectedType"] = type;

        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("file", "Choose a CSV or XLSX file to import.");
            return View();
        }

        await using var stream = file.OpenReadStream();
        var result = await _importService.ImportAsync(stream, file.FileName, type, dryRun, ct);

        return View(result);
    }
}
