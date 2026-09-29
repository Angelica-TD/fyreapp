using FyreApp.Services.DataReset;
using FyreApp.ViewModels.DataReset;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Wipes all client data. Only usable after a Developer switches it on; it switches itself off after each reset.
[Authorize(Roles = "Admin,Developer")]
public class DataResetController : Controller
{
    private readonly IDataResetService _resetService;

    public DataResetController(IDataResetService resetService) => _resetService = resetService;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(new DataResetVm
        {
            Enabled = await _resetService.IsEnabledAsync(ct),
            Counts = await _resetService.GetCountsAsync(ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Developer")]
    public async Task<IActionResult> Toggle(bool enabled, CancellationToken ct)
    {
        await _resetService.SetEnabledAsync(enabled, User.Identity?.Name, ct);
        TempData["Success"] = enabled ? "Data reset enabled." : "Data reset disabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(string? confirmPhrase, CancellationToken ct)
    {
        if (!await _resetService.IsEnabledAsync(ct))
            return Forbid();

        if (!string.Equals(confirmPhrase?.Trim(), DataResetService.ConfirmPhrase, StringComparison.Ordinal))
        {
            TempData["Error"] = $"Type \"{DataResetService.ConfirmPhrase}\" exactly to confirm.";
            return RedirectToAction(nameof(Index));
        }

        var deleted = await _resetService.ResetAsync(User.Identity?.Name, ct);

        return View("Index", new DataResetVm
        {
            Enabled = false,
            Counts = await _resetService.GetCountsAsync(ct),
            Deleted = deleted
        });
    }
}
