using FyreApp.Models;

namespace FyreApp.ViewModels.Lists;

// Contractors, suppliers and users: search, Active (default active) and, for users, their FyreApp login's role
public class DirectoryFilter
{
    public string? Search { get; set; }

    // "active" (default), "inactive" or "any"
    public string Active { get; set; } = "active";

    // Users only: the role of their FyreApp login (Admin, Tech, Developer), or blank for all
    public string? Role { get; set; }

    // Users only: "yes" (has a FyreApp login), "no" or blank for all
    public string? Login { get; set; }

    public int Page { get; set; } = 1;

    public static string NormaliseActive(string? active) =>
        active?.ToLowerInvariant() is "inactive" or "any" ? active.ToLowerInvariant() : "active";

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("search", Search);
        yield return FilterQuery.Kv("active", Active);
        if (!string.IsNullOrWhiteSpace(Role)) yield return FilterQuery.Kv("role", Role);
        if (!string.IsNullOrWhiteSpace(Login)) yield return FilterQuery.Kv("login", Login);
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

    // The FyreApp login linked to this user, if they have one
    public LoginSummary? Login { get; set; }

    // Their security groups, split using the imported group names
    public IReadOnlyList<string> Groups { get; set; } = [];

    // Their licences and accreditations (Uptick accreditations export, matched on name)
    public IReadOnlyList<Accreditation> Accreditations { get; set; } = [];
}

public record LoginSummary(string Id, string? Role, bool IsActive)
{
    public string Label => (Role ?? "No role") + (IsActive ? "" : " (inactive)");
}
