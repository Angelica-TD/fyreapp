namespace FyreApp.Infrastructure;

public static class DateTimeExtensions
{
    private static readonly TimeZoneInfo SydneyTz = 
        TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public static DateTime ToSydney(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, SydneyTz);

    public static DateTime? ToSydney(this DateTime? utc) =>
        utc.HasValue ? utc.Value.ToSydney() : null;
}