using System.Text.Json;
using FyreApp.Data;
using FyreApp.Services.Uptick.Api;
using FyreApp.Services.Uptick.Mapping;
using FyreApp.Services.Uptick.Migration;
using FyreApp.ViewModels.Uptick;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FyreApp.Controllers;

// Copies Uptick's data into FyreApp through Uptick's API (docs/uptick-migration.md §9)
[Authorize(Roles = "Admin")]
public class UptickMigrationController : Controller
{
    private readonly AppDbContext _db;
    private readonly IUptickApiClient _api;
    private readonly IUptickMigrationRunner _runner;
    private readonly UptickApiOptions _options;
    private readonly UptickMapping _mapping;

    public UptickMigrationController(AppDbContext db, IUptickApiClient api, IUptickMigrationRunner runner, IOptions<UptickApiOptions> options,
        UptickMapping mapping)
    {
        _mapping = mapping;
        _db = db;
        _api = api;
        _runner = runner;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new UptickMigrationPageVm
        {
            Configured = _api.IsConfigured,
            BaseUrl = _api.BaseUrl,
            Username = _api.Username,
            MaxRequestsPerMinute = _options.MaxRequestsPerMinute,
            PageSize = _options.PageSize,
            Current = _runner.Current,
            Mapped = _mapping.Resources,
            Endpoints = await _db.UptickEndpointStates.AsNoTracking().OrderBy(s => s.Resource).ToListAsync(ct),
            Runs = await _db.UptickMigrationRuns.AsNoTracking()
                .OrderByDescending(r => r.StartedUtc).Take(15)
                .Select(r => new FyreApp.Models.UptickMigrationRun
                {
                    Id = r.Id, Kind = r.Kind, Resource = r.Resource, StartedBy = r.StartedBy, StartedUtc = r.StartedUtc,
                    FinishedUtc = r.FinishedUtc, Status = r.Status, Error = r.Error, Requests = r.Requests, Throttled = r.Throttled
                })
                .ToListAsync(ct)
        };
        return View(vm);
    }

    // Signs in and reads the endpoint list: one token request and one API request
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        try
        {
            var endpoints = await _api.ListEndpointsAsync(ct);
            TempData["Success"] = $"Connected to {_api.BaseUrl} as {_api.Username}: {endpoints.Count} endpoints listed.";
        }
        catch (Exception ex) when (ex is UptickApiException or UptickRateLimitedException)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Start(UptickRunKind kind, string? resource)
    {
        // A Re-map only reads what's held
        if (!_api.IsConfigured && kind != UptickRunKind.Remap)
            TempData["Error"] = "The Uptick API isn't configured.";
        else if (!_runner.TryStart(kind, string.IsNullOrWhiteSpace(resource) ? null : resource, User.Identity?.Name))
            TempData["Error"] = "A run is already in progress.";
        else
            TempData["Success"] = $"{kind} run started{(string.IsNullOrWhiteSpace(resource) ? "" : $" for {resource}")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cancel()
    {
        _runner.Cancel();
        TempData["Success"] = "Cancelling: the run stops after the request in progress.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Status() => Json(_runner.Current);

    [HttpGet]
    public async Task<IActionResult> Run(int id, CancellationToken ct)
    {
        var run = await _db.UptickMigrationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run == null) return NotFound();

        var results = string.IsNullOrEmpty(run.Report)
            ? []
            : JsonSerializer.Deserialize<List<UptickEndpointResult>>(run.Report) ?? [];
        return View(new UptickRunReportVm { Run = run, Results = results });
    }
}
