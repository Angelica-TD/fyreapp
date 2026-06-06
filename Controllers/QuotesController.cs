using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Quotes;
using FyreApp.ViewModels.Quotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

[Authorize]
public class QuotesController : Controller
{
    private readonly IQuoteService _service;
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public QuotesController(IQuoteService service, AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _db = db;
        _userManager = userManager;
    }

    // ── Index ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var quotes = await _service.GetAllAsync();
        return View(quotes);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new CreateQuoteVm();
        await PopulateCreateViewBagAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateQuoteVm vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCreateViewBagAsync(vm.ClientId);
            return View(vm);
        }

        if (vm.LineItems.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "At least one line item is required.");
            await PopulateCreateViewBagAsync(vm.ClientId);
            return View(vm);
        }

        var userId = _userManager.GetUserId(User)!;
        var result = await _service.CreateAsync(vm, userId);

        return result.Status switch
        {
            QuoteCreateStatus.Success => RedirectToAction(nameof(Details), new { id = result.Quote!.Id }),
            QuoteCreateStatus.ClientNotFound => NotFound(),
            _ => BadRequest(result.Error)
        };
    }

    // ── Details ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Details(int id, bool edit = false)
    {
        var quote = await _service.GetByIdAsync(id);
        if (quote is null) return NotFound();

        var intervals = await _db.MaintenanceIntervals
            .OrderBy(i => i.Months)
            .ToListAsync();

        var vm = new QuoteDetailsVm
        {
            Quote = quote,
            Edit = new EditQuoteVm
            {
                Status = quote.Status,
                ExpiryDate = quote.ExpiryDate,
                Notes = quote.Notes
            },
            Intervals = intervals,
            OpenEdit = edit
        };

        return View(vm);
    }

    // ── Edit (inline POST from Details) ──────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditQuoteVm vm)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Details), new { id, edit = true });

        var result = await _service.UpdateAsync(id, vm);

        if (result.Status == QuoteUpdateStatus.NotFound) return NotFound();

        TempData["Success"] = "Quote updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── Accept / Decline ─────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var result = await _service.AcceptAsync(id);

        TempData[result.Status == QuoteAcceptStatus.Success ? "Success" : "Error"] =
            result.Status == QuoteAcceptStatus.Success
                ? "Quote accepted. Schedules and tasks have been created."
                : result.Error ?? "Could not accept this quote.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id)
    {
        var result = await _service.DeclineAsync(id);

        TempData[result.Status == QuoteUpdateStatus.Success ? "Success" : "Error"] =
            result.Status == QuoteUpdateStatus.Success
                ? "Quote declined."
                : result.Error ?? "Could not decline this quote.";

        return RedirectToAction(nameof(Details), new { id });
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (result.Status == QuoteDeleteStatus.NotFound) return NotFound();

        TempData["Success"] = "Quote deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ── API: assets for a site ────────────────────────────────────────────────

    [HttpGet("/api/quotes/assets/{siteId:int}")]
    public async Task<IActionResult> GetAssetsBySite(int siteId)
    {
        var assets = await _db.Assets
            .Where(a => a.SiteId == siteId)
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync();

        return Ok(assets);
    }

    [HttpGet("/api/quotes/sites/{clientId:int}")]
    public async Task<IActionResult> GetSitesByClient(int clientId)
    {
        var sites = await _db.Sites
            .Where(s => s.ClientId == clientId)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();

        return Ok(sites);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task PopulateCreateViewBagAsync(int? selectedClientId = null)
    {
        ViewBag.Clients = await _db.Clients
            .Where(c => c.Active)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        ViewBag.ServiceTypes = await _db.ServiceTypes
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();

        ViewBag.Intervals = await _db.MaintenanceIntervals
            .OrderBy(i => i.Months)
            .Select(i => new { i.Id, i.Name })
            .ToListAsync();

        if (selectedClientId.HasValue)
        {
            ViewBag.Sites = await _db.Sites
                .Where(s => s.ClientId == selectedClientId.Value)
                .OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync();
        }
        else
        {
            ViewBag.Sites = new List<object>();
        }
    }
}
