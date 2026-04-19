using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.ServiceQuotes;
using FyreApp.ViewModels.ServiceQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

[Authorize]
public class ServiceQuotesController : Controller
{
    private readonly IServiceQuoteService _quotes;
    private readonly AppDbContext _db;

    public ServiceQuotesController(IServiceQuoteService quotes, AppDbContext db)
    {
        _quotes = quotes;
        _db = db;
    }

    // GET: /ServiceQuotes
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var clients = await _db.Clients.Where(c => c.Active).OrderBy(c => c.Name).ToListAsync(ct);

        var vm = new ServiceQuoteIndexVm
        {
            Quotes = await _quotes.GetAllAsync(ct),
            Clients = clients
        };

        if (TempData["OpenCreateModal"] is true)
            vm.OpenCreateModal = true;

        return View(vm);
    }

    // GET: /ServiceQuotes/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var quote = await _quotes.GetByIdAsync(id, ct);
        if (quote is null)
            return NotFound();

        var vm = new ServiceQuoteDetailsVm
        {
            Quote = quote,
            Edit = new UpdateServiceQuoteRequest
            {
                Title = quote.Title,
                Description = quote.Description,
                Status = quote.Status,
                Amount = quote.Amount,
                Notes = quote.Notes,
                ExpiryDate = quote.ExpiryDate
            },
            OpenEdit = TempData["OpenEdit"] as bool? ?? false
        };

        return View(vm);
    }

    // POST: /ServiceQuotes/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(ServiceQuoteIndexVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            vm.Quotes = await _quotes.GetAllAsync(ct);
            vm.Clients = await _db.Clients.Where(c => c.Active).OrderBy(c => c.Name).ToListAsync(ct);
            vm.OpenCreateModal = true;
            return View("Index", vm);
        }

        var result = await _quotes.CreateAsync(vm.Create, ct);

        return result.Status switch
        {
            ServiceQuoteCreateStatus.Success =>
                TempDataSuccess($"Quote created.", RedirectToAction(nameof(Details), new { id = result.QuoteId })),
            ServiceQuoteCreateStatus.ClientNotFound =>
                NotFound(),
            ServiceQuoteCreateStatus.ValidationError =>
                RebuildIndexWithError(result.ErrorMessage ?? "Please check the form and try again.", vm),
            _ => BadRequest()
        };

        IActionResult RebuildIndexWithError(string msg, ServiceQuoteIndexVm indexVm)
        {
            TempData["Error"] = msg;
            indexVm.OpenCreateModal = true;
            return View("Index", indexVm);
        }
    }

    // POST: /ServiceQuotes/Update/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, ServiceQuoteDetailsVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please check the form and try again.";
            TempData["OpenEdit"] = true;
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _quotes.UpdateAsync(id, vm.Edit, ct);

        return result.Status switch
        {
            ServiceQuoteUpdateStatus.Success =>
                TempDataSuccess("Quote updated.", RedirectToAction(nameof(Details), new { id })),
            ServiceQuoteUpdateStatus.NotFound =>
                NotFound(),
            ServiceQuoteUpdateStatus.ValidationError =>
                RedirectWithError(id, result.ErrorMessage ?? "Please check the form and try again."),
            _ => BadRequest()
        };

        IActionResult RedirectWithError(int quoteId, string message)
        {
            TempData["Error"] = message;
            TempData["OpenEdit"] = true;
            return RedirectToAction(nameof(Details), new { id = quoteId });
        }
    }

    // POST: /ServiceQuotes/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _quotes.DeleteAsync(id, ct);

        if (result.Status == ServiceQuoteDeleteStatus.NotFound)
            return NotFound();

        TempData["Success"] = "Quote deleted.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult TempDataSuccess(string message, IActionResult redirect)
    {
        TempData["Success"] = message;
        return redirect;
    }
}
