namespace FyreApp.ViewModels.Lists;

// The bar above a list: count, Edit N / Clear selection / Select all N, and Download (N)
public class SelectionToolbarVm
{
    public int Total { get; set; }
    public int PageCount { get; set; }

    // Plural, e.g. "Routines", "Assets"
    public string Noun { get; set; } = "Items";
    public string NounSingular { get; set; } = "Item";

    public string DownloadUrl { get; set; } = "";

    // Modal opened by Edit; null = no Edit (download only)
    public string? EditModalId { get; set; }
}

// Filters in effect on a list page, as query values (f=true + defaults included), for the download link
// and the bulk form, so both act on exactly what's listed
public static class FilterQuery
{
    public static string ToQueryString(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    public static KeyValuePair<string, string> Kv(string key, string? value) => new(key, value ?? "");

    public static string YesNoAny(bool? value) => value switch { true => "yes", false => "no", _ => "any" };
}
