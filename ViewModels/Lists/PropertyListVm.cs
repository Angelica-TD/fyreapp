namespace FyreApp.ViewModels.Lists;

// Defaults match Uptick's Properties list: status Active, Setup or On hold
public class PropertyFilter
{
    public string? Search { get; set; }
    public List<string> Status { get; set; } = ListFilters.NotInactive();
    public int Page { get; set; } = 1;

    public int ActiveCount => Status.Count > 0 ? 1 : 0;

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("f", "true");
        yield return FilterQuery.Kv("search", Search);
        foreach (var s in Status) yield return FilterQuery.Kv("status", s);
    }
}

public class PropertyListItemVm
{
    public int Id { get; set; }
    public string? Ref { get; set; }
    public string? Status { get; set; }
    public DateTime? Created { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? State { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientContact { get; set; }
}

public class PropertyListVm
{
    public PropertyFilter Filter { get; set; } = new();
    public IReadOnlyList<PropertyListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);
}
