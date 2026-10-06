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
}
