namespace FyreApp.Infrastructure;

// An asset's last service result (Uptick's "Compliant": Pass / Fail / N/A), as Uptick badges it:
// "PASS" in lists, "COMPLIANT" on the asset page
public static class AssetCompliance
{
    public static (string Text, string Css)? ListBadge(string? compliance) => Normalise(compliance) switch
    {
        "pass" => ("PASS", "bg-success"),
        "fail" => ("FAIL", "bg-danger"),
        "n/a" => ("N/A", "bg-secondary"),
        null => null,
        var other => (other.ToUpperInvariant(), "bg-secondary")
    };

    public static (string Text, string Css)? PageBadge(string? compliance) => Normalise(compliance) switch
    {
        "pass" => ("COMPLIANT", "bg-success"),
        "fail" => ("NON-COMPLIANT", "bg-danger"),
        "n/a" => ("N/A", "bg-secondary"),
        null => null,
        var other => (other.ToUpperInvariant(), "bg-secondary")
    };

    private static string? Normalise(string? c) => c?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "true" or "yes" or "pass" => "pass",
        "false" or "no" or "fail" => "fail",
        var v => v
    };

    // "1 month ago", as Uptick says when a defect was identified
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var days = (int)(nowUtc - utc).TotalDays;
        static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
        return days switch
        {
            < 1 => "today",
            < 30 => Plural(days, "day"),
            < 365 => Plural(Math.Max(1, days / 30), "month"),
            _ => Plural(days / 365, "year")
        };
    }
}
