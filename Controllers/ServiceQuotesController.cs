using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Clients;
using FyreApp.Services.Email;
using FyreApp.Services.ServiceQuotes;
using FyreApp.Services.Sites;
using FyreApp.ViewModels.ServiceQuotes;
using FyreApp.ViewModels.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

[Authorize]
public class ServiceQuotesController : Controller
{
    private readonly IServiceQuoteService _quotes;
    private readonly IClientService _clients;
    private readonly IEmailService _email;
    private readonly SitesService _sites;
    private readonly AppDbContext _db;

    public ServiceQuotesController(
        IServiceQuoteService quotes,
        IClientService clients,
        IEmailService email,
        SitesService sites,
        AppDbContext db)
    {
        _quotes = quotes;
        _clients = clients;
        _email = email;
        _sites = sites;
        _db = db;
    }

    // ── Index ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new ServiceQuoteIndexVm
        {
            Quotes = await _quotes.GetAllAsync(ct),
            Clients = await _db.Clients.Where(c => c.Active).OrderBy(c => c.Name).ToListAsync(ct),
            Intervals = await _db.MaintenanceIntervals.OrderBy(i => i.Months).ToListAsync(ct),
            ServiceOfferings = await _db.ServiceOfferings.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct)
        };
        return View(vm);
    }

    // ── Details ──────────────────────────────────────────────────────────────

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
                QuoteType = quote.QuoteType,
                MaintenanceIntervalId = quote.MaintenanceIntervalId,
                Amount = quote.Amount,
                Notes = quote.Notes,
                ExpiryDate = quote.ExpiryDate
            },
            Intervals = await _db.MaintenanceIntervals.OrderBy(i => i.Months).ToListAsync(ct),
            OpenEdit = TempData["OpenEdit"] as bool? ?? false
        };

        return View(vm);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(ServiceQuoteIndexVm vm, CancellationToken ct)
    {
        int clientId;

        if (vm.CreateNewClient)
        {
            // Validate inline client fields manually
            var clientName = vm.NewClient?.Name?.Trim();
            var contactName = vm.NewClient?.PrimaryContactName?.Trim();
            var contactEmail = vm.NewClient?.PrimaryContactEmail?.Trim();
            var contactMobile = vm.NewClient?.PrimaryContactMobile?.Trim();

            if (string.IsNullOrWhiteSpace(clientName))
                ModelState.AddModelError("NewClient.Name", "Client name is required.");

            if (string.IsNullOrWhiteSpace(contactEmail) && string.IsNullOrWhiteSpace(contactMobile))
                ModelState.AddModelError("NewClient.PrimaryContactEmail", "Email or mobile is required.");

            // Suppress irrelevant ClientId error
            ModelState.Remove("Create.ClientId");

            if (!ModelState.IsValid)
                return await RebuildIndexView(vm, ct);

            var createClientVm = new ViewModels.Clients.CreateClientVm
            {
                Name = clientName,
                PrimaryContactName = contactName,
                PrimaryContactEmail = contactEmail,
                PrimaryContactMobile = contactMobile
            };

            var clientResult = await _clients.CreateAsync(createClientVm, ct);

            if (clientResult.Status == ClientCreateStatus.DuplicateName)
            {
                ModelState.AddModelError("NewClient.Name", $"A client named \"{clientName}\" already exists.");
                return await RebuildIndexView(vm, ct);
            }

            if (clientResult.Status != ClientCreateStatus.Success || clientResult.ClientId is null)
            {
                ModelState.AddModelError(string.Empty, clientResult.ErrorMessage ?? "Failed to create client.");
                return await RebuildIndexView(vm, ct);
            }

            clientId = clientResult.ClientId.Value;

            // Property is required when creating a new client
            var siteName = vm.NewSite?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(siteName))
            {
                ModelState.AddModelError("NewSite.Name", "Property name is required.");
                // Roll back the client we just created so there are no orphans
                var orphan = await _db.Clients.FindAsync([clientId], ct);
                if (orphan is not null) _db.Clients.Remove(orphan);
                await _db.SaveChangesAsync(ct);
                return await RebuildIndexView(vm, ct);
            }

            var hasGoogle = !string.IsNullOrWhiteSpace(vm.NewSite?.Google?.PlaceId);
            var hasManual = !string.IsNullOrWhiteSpace(vm.NewSite?.Manual?.AddressLine1);

            if (!hasGoogle && !hasManual)
            {
                ModelState.AddModelError("NewSite.Name", "Property address is required. Select from the autocomplete or enter manually.");
                var orphan = await _db.Clients.FindAsync([clientId], ct);
                if (orphan is not null) _db.Clients.Remove(orphan);
                await _db.SaveChangesAsync(ct);
                return await RebuildIndexView(vm, ct);
            }

            var siteReq = new CreateSiteRequest
            {
                ClientId = clientId,
                Name = siteName,
                Google = new GoogleAddressInput
                {
                    PlaceId = vm.NewSite!.Google.PlaceId,
                    FormattedAddress = vm.NewSite.Google.FormattedAddress
                },
                Manual = new ManualAddressInput
                {
                    AddressLine1 = vm.NewSite.Manual.AddressLine1,
                    AddressLine2 = vm.NewSite.Manual.AddressLine2,
                    Suburb = vm.NewSite.Manual.Suburb,
                    State = vm.NewSite.Manual.State,
                    Postcode = vm.NewSite.Manual.Postcode
                }
            };

            var siteResult = await _sites.CreateAsync(siteReq, ct);
            if (siteResult.Status is CreateSiteStatus.ValidationError or CreateSiteStatus.GeocodeFailed)
            {
                ModelState.AddModelError("NewSite.Name", siteResult.Error ?? "Could not save property. Please check the address.");
                var orphan = await _db.Clients.FindAsync([clientId], ct);
                if (orphan is not null) _db.Clients.Remove(orphan);
                await _db.SaveChangesAsync(ct);
                return await RebuildIndexView(vm, ct);
            }

            // Use the newly created site's ID for the quote
            vm.Create.SiteId = siteResult.site?.Id;
        }
        else
        {
            if (vm.Create.ClientId is null or 0)
            {
                ModelState.AddModelError("Create.ClientId", "Client is required.");
                return await RebuildIndexView(vm, ct);
            }

            clientId = vm.Create.ClientId.Value;

            if (vm.Create.SiteId is null or 0)
            {
                ModelState.AddModelError("Create.SiteId", "Property is required.");
                return await RebuildIndexView(vm, ct);
            }
        }

        // Validate service offering
        if (vm.Create.ServiceOfferingId is null or 0)
        {
            ModelState.AddModelError("Create.ServiceOfferingId", "Service is required.");
            return await RebuildIndexView(vm, ct);
        }

        // Validate quote fields
        if (!ModelState.IsValid)
            return await RebuildIndexView(vm, ct);

        // Default expiry to 30 days if not provided
        if (vm.Create.ExpiryDate is null)
            vm.Create.ExpiryDate = DateTime.UtcNow.AddDays(30);

        var result = await _quotes.CreateAsync(vm.Create, clientId, vm.Create.SiteId, ct);

        return result.Status switch
        {
            ServiceQuoteCreateStatus.Success =>
                TempDataSuccess("Quote created.", RedirectToAction(nameof(Details), new { id = result.QuoteId })),
            ServiceQuoteCreateStatus.ClientNotFound =>
                NotFound(),
            ServiceQuoteCreateStatus.ValidationError =>
                TempDataError(result.ErrorMessage ?? "Please check the form.", RedirectToAction(nameof(Index))),
            _ => BadRequest()
        };
    }

    // ── Update ───────────────────────────────────────────────────────────────

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
                RedirectWithEditError(id, result.ErrorMessage ?? "Please check the form."),
            _ => BadRequest()
        };
    }

    // ── Delete ───────────────────────────────────────────────────────────────

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

    // ── API: sites for a client (used by create modal) ───────────────────────

    [HttpGet("/api/client-sites/{clientId:int}")]
    public async Task<IActionResult> GetClientSites(int clientId, CancellationToken ct)
    {
        var sites = await _db.Sites
            .Where(s => s.ClientId == clientId)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        return Json(sites);
    }

    // ── Send to client ───────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Send(int id, CancellationToken ct)
    {
        var prepared = await _quotes.PrepareForSendAsync(id, ct);

        if (prepared.Status == PrepareQuoteForSendStatus.NotFound)
            return NotFound();

        if (prepared.Status == PrepareQuoteForSendStatus.NoClientEmail)
            return RedirectWithError(id, prepared.ErrorMessage!);

        var clientViewUrl = Url.Action(
            nameof(ClientView), "ServiceQuotes",
            new { token = prepared.Token },
            Request.Scheme,
            Request.Host.ToString());

        var html = BuildQuoteEmail(prepared.Quote!, clientViewUrl!, prepared.ClientName!);

        await _email.SendAsync(
            prepared.ClientEmail!,
            prepared.ClientName!,
            $"Service Quote {prepared.Quote!.QuoteNumber} — {prepared.Quote.Title}",
            html,
            ct);

        return TempDataSuccess("Quote sent to client.", RedirectToAction(nameof(Details), new { id }));
    }

    // ── Public client view (no auth) ─────────────────────────────────────────

    [HttpGet("/q/{token:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ClientView(Guid token, CancellationToken ct)
    {
        var quote = await _quotes.GetByTokenAsync(token, ct);
        if (quote is null)
            return NotFound();

        return View(quote);
    }

    [HttpPost("/q/{token:guid}/approve")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid token, CancellationToken ct)
    {
        var result = await _quotes.ApproveAsync(token, ct);

        if (result.Status == ServiceQuoteUpdateStatus.NotFound)
            return NotFound();

        TempData["QuoteAction"] = "approved";
        return RedirectToAction(nameof(ClientView), new { token });
    }

    [HttpPost("/q/{token:guid}/decline")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(Guid token, CancellationToken ct)
    {
        var result = await _quotes.DeclineAsync(token, ct);

        if (result.Status == ServiceQuoteUpdateStatus.NotFound)
            return NotFound();

        TempData["QuoteAction"] = "declined";
        return RedirectToAction(nameof(ClientView), new { token });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IActionResult TempDataSuccess(string message, IActionResult redirect)
    {
        TempData["Success"] = message;
        return redirect;
    }

    private IActionResult TempDataError(string message, IActionResult redirect)
    {
        TempData["Error"] = message;
        return redirect;
    }

    private IActionResult RedirectWithEditError(int id, string message)
    {
        TempData["Error"] = message;
        TempData["OpenEdit"] = true;
        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult RedirectWithError(int id, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> RebuildIndexView(ServiceQuoteIndexVm vm, CancellationToken ct)
    {
        vm.Quotes = await _quotes.GetAllAsync(ct);
        vm.Clients = await _db.Clients.Where(c => c.Active).OrderBy(c => c.Name).ToListAsync(ct);
        vm.Intervals = await _db.MaintenanceIntervals.OrderBy(i => i.Months).ToListAsync(ct);
        vm.ServiceOfferings = await _db.ServiceOfferings.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);
        vm.OpenCreateModal = true;
        return View("Index", vm);
    }

    private static string BuildQuoteEmail(ServiceQuote quote, string clientViewUrl, string clientName)
    {
        var quoteTypeName = quote.QuoteType == ServiceQuoteType.OneTime ? "One-Time" : "Routine / Contract";
        var expiry = quote.ExpiryDate.HasValue
            ? $"<tr><td style='padding:6px 12px;color:#6b7280;'>Expires</td><td style='padding:6px 12px;'>{quote.ExpiryDate.Value:dd MMM yyyy}</td></tr>"
            : string.Empty;

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"/></head>
            <body style="margin:0;padding:0;background:#f3f4f6;font-family:Arial,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f3f4f6;padding:32px 0;">
                <tr><td align="center">
                  <table width="600" cellpadding="0" cellspacing="0" style="background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.08);">

                    <!-- Header -->
                    <tr><td style="background:#e55b2d;padding:24px 32px;">
                      <h1 style="margin:0;color:#fff;font-size:22px;">FyreApp</h1>
                    </td></tr>

                    <!-- Body -->
                    <tr><td style="padding:32px;">
                      <p style="margin:0 0 16px;color:#111;">Hi {System.Net.WebUtility.HtmlEncode(clientName)},</p>
                      <p style="margin:0 0 24px;color:#374151;">
                        Please review the service quote below. You can approve or decline it using the button at the bottom of this email.
                      </p>

                      <table width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e5e7eb;border-radius:6px;overflow:hidden;margin-bottom:24px;">
                        <tr style="background:#f9fafb;">
                          <td colspan="2" style="padding:10px 12px;font-weight:600;color:#111;">
                            Quote {System.Net.WebUtility.HtmlEncode(quote.QuoteNumber)}
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:6px 12px;color:#6b7280;">Title</td>
                          <td style="padding:6px 12px;">{System.Net.WebUtility.HtmlEncode(quote.Title)}</td>
                        </tr>
                        <tr style="background:#f9fafb;">
                          <td style="padding:6px 12px;color:#6b7280;">Type</td>
                          <td style="padding:6px 12px;">{quoteTypeName}</td>
                        </tr>
                        <tr>
                          <td style="padding:6px 12px;color:#6b7280;">Amount</td>
                          <td style="padding:6px 12px;font-weight:600;">{quote.Amount:C}</td>
                        </tr>
                        {expiry}
                        {(string.IsNullOrWhiteSpace(quote.Description) ? "" : $"<tr style='background:#f9fafb;'><td style='padding:6px 12px;color:#6b7280;'>Description</td><td style='padding:6px 12px;'>{System.Net.WebUtility.HtmlEncode(quote.Description)}</td></tr>")}
                      </table>

                      <div style="text-align:center;">
                        <a href="{clientViewUrl}"
                           style="display:inline-block;padding:12px 28px;background:#e55b2d;color:#fff;text-decoration:none;border-radius:6px;font-weight:600;font-size:15px;">
                          Review &amp; Respond to Quote
                        </a>
                      </div>

                      <p style="margin:24px 0 0;color:#6b7280;font-size:13px;">
                        Or copy this link into your browser:<br/>
                        <a href="{clientViewUrl}" style="color:#e55b2d;word-break:break-all;">{clientViewUrl}</a>
                      </p>
                    </td></tr>

                    <!-- Footer -->
                    <tr><td style="padding:16px 32px;background:#f9fafb;color:#9ca3af;font-size:12px;text-align:center;border-top:1px solid #e5e7eb;">
                      This email was sent by FyreApp on behalf of your service provider.
                    </td></tr>

                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
