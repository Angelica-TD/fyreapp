namespace FyreApp.ViewModels.Lists;

// Bits shared by the Uptick-style list pages (Routines, Assets, Properties, Remarks, Reports)
public static class ListFilters
{
    public const int PageSize = 50;

    // Uptick property statuses, in Uptick's order
    public static readonly string[] PropertyStatuses = { "ACTIVE", "SETUP", "ONHOLD", "INACTIVE" };

    // Uptick's default property filter on Assets, Properties and Remarks: everything but inactive
    public static List<string> NotInactive() => new() { "ACTIVE", "SETUP", "ONHOLD" };

    public static string PropertyStatusLabel(string? status) => status?.ToUpperInvariant() switch
    {
        null or "" => "—",
        "ONHOLD" => "On hold",
        var s => s[0] + s[1..].ToLowerInvariant()
    };

    public static int Pages(int total) => Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

    public static int ClampPage(int page, int total) => Math.Clamp(page, 1, Pages(total));
}
