using System.Globalization;

namespace FyreApp.Infrastructure;

public static class DateTimeExtensions
{
    private static readonly TimeZoneInfo SydneyTz =
        TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public static DateTime ToSydney(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, SydneyTz);

    public static DateTime? ToSydney(this DateTime? utc) =>
        utc.HasValue ? utc.Value.ToSydney() : null;

    // How dates are shown everywhere, e.g. "01 Oct 2026" (unambiguous, unlike 10/01/2026)
    public const string DateFormat = "dd MMM yyyy";
    public const string DateTimeFormat = "dd MMM yyyy, h:mm tt";

    public static string ToDisplayDate(this DateTime date) =>
        date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string? ToDisplayDate(this DateTime? date) =>
        date?.ToDisplayDate();

    public static string ToDisplayDateTime(this DateTime date) =>
        date.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    public static string? ToDisplayDateTime(this DateTime? date) =>
        date?.ToDisplayDateTime();

    // As Uptick writes them on timelines and asset pages: "7th Sep 2026", "13th Oct 2026 2:26pm"
    public static string ToOrdinalDate(this DateTime date) =>
        $"{date.Day}{OrdinalSuffix(date.Day)} {date.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";

    public static string? ToOrdinalDate(this DateTime? date) => date?.ToOrdinalDate();

    public static string ToOrdinalDateTime(this DateTime date) =>
        $"{date.ToOrdinalDate()} {date.ToString("h:mm", CultureInfo.InvariantCulture)}{(date.Hour < 12 ? "am" : "pm")}";

    private static string OrdinalSuffix(int day) => day switch
    {
        11 or 12 or 13 => "th",
        _ when day % 10 == 1 => "st",
        _ when day % 10 == 2 => "nd",
        _ when day % 10 == 3 => "rd",
        _ => "th"
    };
}
