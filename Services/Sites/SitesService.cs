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
            return new(CreateSiteStatus.ValidationError, Error: "Client is required.");

        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new(CreateSiteStatus.ValidationError, Error: "Property name is required.");

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == req.ClientId, ct);
        if (!clientExists)
            return new(CreateSiteStatus.NotFound);

        // Prefer Google place id if present
        var placeId = (req.Google.PlaceId ?? string.Empty).Trim();

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
                return new(CreateSiteStatus.GeocodeFailed, Error: "Could not validate the selected address. Please enter it manually.");

            site.GooglePlaceId = placeId;
            site.AddressDisplay = g.AddressDisplay?.Trim();
            site.AddressLine1 = g.AddressLine1?.Trim();
            site.Suburb = g.Suburb?.Trim();
            site.State = g.State?.Trim();
            site.Postcode = g.Postcode?.Trim();
        }
        else
        {
            // Manual fallback (minimal validation)
            var line1 = (req.Manual.AddressLine1 ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(req.Google.FormattedAddress))
            {
                // Optional: allow "free-typed" address to seed manual line1 if they didn't select a suggestion
                if (string.IsNullOrWhiteSpace(line1))
                    line1 = req.Google.FormattedAddress.Trim();
            }

            // If they provided *any* manual data, accept it.
            site.AddressLine1 = string.IsNullOrWhiteSpace(line1) ? null : line1;
            site.AddressLine2 = string.IsNullOrWhiteSpace(req.Manual.AddressLine2) ? null : req.Manual.AddressLine2.Trim();
            site.Suburb = string.IsNullOrWhiteSpace(req.Manual.Suburb) ? null : req.Manual.Suburb.Trim();
            site.State = string.IsNullOrWhiteSpace(req.Manual.State) ? null : req.Manual.State.Trim();
            site.Postcode = string.IsNullOrWhiteSpace(req.Manual.Postcode) ? null : req.Manual.Postcode.Trim();

            site.AddressDisplay = BuildDisplay(site);
        }

        _db.Sites.Add(site);
        await _db.SaveChangesAsync(ct);

        return new(CreateSiteStatus.Success, SiteId: site.Id);
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
}