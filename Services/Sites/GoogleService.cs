using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FyreApp.Services.Sites;

public sealed class GoogleMapsOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class GoogleGeocodingClient
{
    private readonly HttpClient _http;
    private readonly GoogleMapsOptions _opt;

    public GoogleGeocodingClient(HttpClient http, IOptions<GoogleMapsOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public async Task<GeocodedAddress?> GeocodePlaceIdAsync(string placeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            return null;

        var url =
            $"https://maps.googleapis.com/maps/api/geocode/json?place_id={Uri.EscapeDataString(placeId)}&key={Uri.EscapeDataString(_opt.ApiKey)}";

        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        var status = root.GetProperty("status").GetString();

        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            return null;

        var first = root.GetProperty("results")[0];

        var formatted = first.TryGetProperty("formatted_address", out var fa) ? fa.GetString() : null;

        string? line1 = null, suburb = null, state = null, postcode = null;

        foreach (var c in first.GetProperty("address_components").EnumerateArray())
        {
            var longName = c.GetProperty("long_name").GetString();
            var shortName = c.GetProperty("short_name").GetString();

            var types = c.GetProperty("types").EnumerateArray()
                .Select(t => t.GetString())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (types.Contains("street_number"))
                line1 = (longName + " " + (line1 ?? "")).Trim();

            if (types.Contains("route"))
                line1 = ((line1 ?? "") + " " + longName).Trim();

            if (types.Contains("locality"))
                suburb = longName;

            if (types.Contains("administrative_area_level_1"))
                state = shortName ?? longName;

            if (types.Contains("postal_code"))
                postcode = longName;
        }

        return new GeocodedAddress
        {
            AddressDisplay = formatted,
            AddressLine1 = line1,
            Suburb = suburb,
            State = state,
            Postcode = postcode
        };
    }
}

public sealed class GeocodedAddress
{
    public string? AddressDisplay { get; set; }
    public string? AddressLine1 { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
}