using FyreApp.Models;

namespace FyreApp.ViewModels.Lists;

// Contractors, suppliers and Uptick users: search, Active (default active) and, for users, licence type
public class DirectoryFilter
{
    public string? Search { get; set; }

    // "active" (default), "inactive" or "any"
    public string Active { get; set; } = "active";

    // Users only: DESK, FIELD, CONTRACTOR, CUSTOMER or blank for all
    public string? License { get; set; }

    public int Page { get; set; } = 1;

    public static string NormaliseActive(string? active) =>
        active?.ToLowerInvariant() is "inactive" or "any" ? active.ToLowerInvariant() : "active";

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("search", Search);
        yield return FilterQuery.Kv("active", Active);
        if (!string.IsNullOrWhiteSpace(License)) yield return FilterQuery.Kv("license", License);
    }
}

public class DirectoryListVm<T>
{
    public DirectoryFilter Filter { get; set; } = new();
    public IReadOnlyList<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);
}

public class UptickUserListItemVm
{
    public UptickUser User { get; set; } = null!;

    // A FyreApp login with the same email
    public bool HasFyreAppLogin { get; set; }
}
