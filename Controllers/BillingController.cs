using FyreApp.Models;
using FyreApp.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Uptick's invoices, defect quotes and sales quotes, one page each (lines and PDF), under their property
[Authorize(Roles = "Admin")]
public class BillingController : Controller
{
    private readonly IBillingPageService _pages;
    public BillingController(IBillingPageService pages) => _pages = pages;

    [HttpGet]
    public async Task<IActionResult> Invoice(int id, CancellationToken ct = default)
    {
        if (await _pages.InvoiceAsync(id, ct) is not { } vm) return NotFound();
        Crumbs(vm.Invoice.Property, "billing", vm.Invoice.Number);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Quote(int id, CancellationToken ct = default)
    {
        if (await _pages.QuoteAsync(id, ct) is not { } vm) return NotFound();
        Crumbs(vm.Quote.Property, "records", vm.Quote.Reference);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> SalesQuote(int id, CancellationToken ct = default)
    {
        if (await _pages.SalesQuoteAsync(id, ct) is not { } vm) return NotFound();
        Crumbs(vm.Quote.Property, "records", vm.Quote.Ref);
        return View(vm);
    }

    // Home / Properties / P-0016 / IV000032831
    private void Crumbs(Property? property, string tab, string name)
    {
        var crumbs = new List<(string Text, string? Url)>
        {
            ("Home", Url.Action("Index", "Home")),
            ("Properties", Url.Action("Index", "Property"))
        };
        if (property != null)
            crumbs.Add((property.DisplayRef ?? property.Name, Url.Action("Details", "Property", new { id = property.Id, tab })));
        crumbs.Add((name, null));
        ViewData["Breadcrumbs"] = crumbs;
    }
}
