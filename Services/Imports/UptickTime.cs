using System.Globalization;
using System.Text.RegularExpressions;

namespace FyreApp.Services.Imports;

// Uptick exports timestamps in local Queensland time (UTC+10, no daylight saving) unless the value carries an offset.
public static class UptickTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd",
        "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy", "dd/MM/yyyy"
    };

    private static readonly Regex OffsetSuffix = new(@"(Z|[+-]\d\d:?\d\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DateTime? ParseToUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();

        if (OffsetSuffix.IsMatch(v) &&
            DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return dto.UtcDateTime;

        if (DateTime.TryParseExact(v, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ||
            DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

        return null;
    }

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane"); }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("AEST", TimeSpan.FromHours(10), "AEST", "AEST");
        }
    }
}
