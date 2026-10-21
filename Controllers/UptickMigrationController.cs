using System.Text.Json;
using FyreApp.Data;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Files;
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
[Authorize(Roles = "Developer")]
public class UptickMigrationController : Controller
{
    private readonly AppDbContext _db;
    private readonly IUptickApiClient _api;
    private readonly IUptickMigrationRunner _runner;
    private readonly UptickApiOptions _options;
    private readonly UptickMapping _mapping;
    private readonly IFileStore _store;

    public UptickMigrationController(AppDbContext db, IUptickApiClient api, IUptickMigrationRunner runner, IOptions<UptickApiOptions> options,
        UptickMapping mapping, IFileStore store)
    {
        _store = store;
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
            StorageConfigured = _store.IsConfigured,
            Storage = _store.Description,
            Files = await FileCountsAsync(ct),
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

    private async Task<List<UptickFileCounts>> FileCountsAsync(CancellationToken ct)
    {
        var sources = UptickFileDownloader.PdfSources
            .Select(p => (Source: p.Source, Label: char.ToUpperInvariant(p.Noun[0]) + p.Noun[1..] + " PDFs"))
            .Append((Source: UptickFileDownloader.Documents, Label: "Document files (photos, signatures, attachments)"))
            .ToList();
        var resources = sources.Select(s => s.Source).ToList();
        var listed = await _db.UptickRecords.AsNoTracking()
            .Where(r => resources.Contains(r.Resource) && r.DeletedUtc == null)
            .GroupBy(r => r.Resource).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var held = await _db.UptickFiles.AsNoTracking()
            .GroupBy(f => f.Source)
            .Select(g => new
            {
                g.Key,
                Downloaded = g.Count(f => f.DownloadedUtc != null),
                Bytes = g.Where(f => f.DownloadedUtc != null).Sum(f => f.Size ?? 0),
                NoFile = g.Count(f => f.NoFile),
                Failing = g.Count(f => f.DownloadedUtc == null && f.Error != null)
            })
            .ToDictionaryAsync(g => g.Key, ct);
        return sources
            .Select(s => held.TryGetValue(s.Source, out var h)
                ? new UptickFileCounts(s.Source, s.Label, listed.GetValueOrDefault(s.Source), h.Downloaded, h.Bytes, h.NoFile, h.Failing)
                : new UptickFileCounts(s.Source, s.Label, listed.GetValueOrDefault(s.Source), 0, 0, 0, 0))
            .ToList();
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

    // Writes a small file to the file store and reads it back
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestStorage(CancellationToken ct)
    {
        var (ok, message) = await FileStores.TestAsync(_store, ct: ct);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Start(UptickRunKind kind, string? resource, int? limit)
    {
        if (limit is <= 0) limit = null;
        // A Re-map only reads what's held
        if (!_api.IsConfigured && kind != UptickRunKind.Remap)
            TempData["Error"] = "The Uptick API isn't configured.";
        else if (kind == UptickRunKind.Files && !_store.IsConfigured)
            TempData["Error"] = "File storage isn't set up (Storage:Provider).";
        else if (!_runner.TryStart(kind, string.IsNullOrWhiteSpace(resource) ? null : resource, User.Identity?.Name,
                     kind == UptickRunKind.Files ? limit : null))
            TempData["Error"] = "A run is already in progress.";
        else
            TempData["Success"] = $"{kind} run started{(string.IsNullOrWhiteSpace(resource) ? "" : $" for {resource}")}" +
                                  $"{(kind == UptickRunKind.Files && limit is { } n ? $": the first {n} of each kind" : "")}.";
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
