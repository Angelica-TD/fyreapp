using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Sites;

namespace FyreApp.Services.Sites;

public sealed class SitesService
{
    private readonly AppDbContext _db;
    private readonly GoogleGeocodingClient _geo;

    public SitesService(AppDbContext db, GoogleGeocodingClient geo)
    {
        _db = db;
        _geo = geo;
    }

    public async Task<CreateSiteResult> CreateAsync(CreateSiteRequest req, CancellationToken ct)
    {
        if (req.ClientId <= 0)
            return new(CreateSiteStatus.ValidationError, null, Error: "Client is required.");

        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new(CreateSiteStatus.ValidationError, null, Error: "Property name is required.");

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == req.ClientId, ct);
        if (!clientExists)
            return new(CreateSiteStatus.NotFound, null, Error: "Client not found");

        var siteNameExists = await _db.Sites.AnyAsync(s =>
            s.ClientId == req.ClientId &&
            s.Name.ToLower() == name.ToLower(),
            ct);
            
        if (siteNameExists)
            return new(CreateSiteStatus.DuplicateName, null, Error: "A Property with this name already exists");

        // Prefer Google place ID when provided (most reliable)
        var placeId = NT(req.Google?.PlaceId);
        if (placeId is not null)
        {
            var existing = await _db.Sites
                .FirstOrDefaultAsync(s =>
                    s.ClientId == req.ClientId &&
                    s.GooglePlaceId == placeId,
                    ct);

            if (existing != null)
                return new(CreateSiteStatus.Success, existing);

        }
        else
        {
            // Manual address duplicate -> return existing site (treat as success)
            var line1 = req.Manual?.AddressLine1?.Trim();
            var line2 = req.Manual?.AddressLine2?.Trim();
            var suburb = req.Manual?.Suburb?.Trim();
            var state = req.Manual?.State?.Trim();
            var postcode = req.Manual?.Postcode?.Trim();

            // Only attempt duplicate detection if we have a meaningful manual address
            if (!string.IsNullOrWhiteSpace(line1) &&
                !string.IsNullOrWhiteSpace(suburb) &&
                !string.IsNullOrWhiteSpace(state) &&
                !string.IsNullOrWhiteSpace(postcode))
            {
                var line1Norm = line1.ToLower();
                var line2Norm = (line2 ?? string.Empty).ToLower();
                var suburbNorm = suburb.ToLower();
                var stateNorm = state.ToLower();
                var postcodeNorm = postcode.ToLower();

                var existing = await _db.Sites.FirstOrDefaultAsync(s =>
                    s.ClientId == req.ClientId &&
                    s.AddressLine1 != null &&
                    s.Suburb != null &&
                    s.State != null &&
                    s.Postcode != null &&
                    s.AddressLine1.Trim().ToLower() == line1Norm &&
                    (s.AddressLine2 ?? string.Empty).Trim().ToLower() == line2Norm &&
                    s.Suburb.Trim().ToLower() == suburbNorm &&
                    s.State.Trim().ToLower() == stateNorm &&
                    s.Postcode.Trim().ToLower() == postcodeNorm,
                    ct);

                if (existing is not null)
                    return new(CreateSiteStatus.Success, existing);
            }
        }

        var site = new Site
        {
            ClientId = req.ClientId,
            Name = name

            // AddressDisplay = req.Google.FormattedAddress,
            // AddressLine1 = req.Manual.AddressLine1,
            // AddressLine2 = req.Manual.AddressLine2,
            // Suburb = req.Manual.Suburb,
            // State = req.Manual.State,
            // Postcode = req.Manual.Postcode

        };

        if (!string.IsNullOrWhiteSpace(placeId))
        {
            var g = await _geo.GeocodePlaceIdAsync(placeId, ct);
            if (g is null)
                return new(CreateSiteStatus.GeocodeFailed, null, Error: "Could not validate the selected address. Please enter it manually.");

            site.GooglePlaceId = placeId;
            site.AddressDisplay = g.AddressDisplay?.Trim();
            site.AddressLine1 = g.AddressLine1?.Trim();
            site.Suburb = g.Suburb?.Trim();
            site.State = g.State?.Trim();
            site.Postcode = g.Postcode?.Trim();
        }
        else
        {
            var line1 = (req.Manual.AddressLine1 ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(req.Google.FormattedAddress))
            {
                if (string.IsNullOrWhiteSpace(line1))
                    line1 = req.Google.FormattedAddress.Trim();
            }

            site.AddressLine1 = string.IsNullOrWhiteSpace(line1) ? null : line1;
            site.AddressLine2 = string.IsNullOrWhiteSpace(req.Manual.AddressLine2) ? null : req.Manual.AddressLine2.Trim();
            site.Suburb = string.IsNullOrWhiteSpace(req.Manual.Suburb) ? null : req.Manual.Suburb.Trim();
            site.State = string.IsNullOrWhiteSpace(req.Manual.State) ? null : req.Manual.State.Trim();
            site.Postcode = string.IsNullOrWhiteSpace(req.Manual.Postcode) ? null : req.Manual.Postcode.Trim();

            site.AddressDisplay = BuildDisplay(site);
        }

        _db.Sites.Add(site);
        await _db.SaveChangesAsync(ct);

        return new(CreateSiteStatus.Success, site, SiteName: site.Name);
    }

    private static string? BuildDisplay(Site s)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(s.AddressLine1)) parts.Add(s.AddressLine1!);
        if (!string.IsNullOrWhiteSpace(s.AddressLine2)) parts.Add(s.AddressLine2!);

        var tail = string.Join(" ", new[] { s.Suburb, s.State, s.Postcode }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        if (!string.IsNullOrWhiteSpace(tail)) parts.Add(tail);

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string N(string? s) => (s ?? string.Empty).Trim().ToUpperInvariant();
    private static string? NT(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}