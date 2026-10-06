namespace FyreApp.ViewModels.Lists;

// Uptick's Reports list has no default filters, only search
public class ReportFilter
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("search", Search);
    }
}

public class ReportListItemVm
{
    public string? Ref { get; set; }
    public string? Title { get; set; }
    public bool? Compliant { get; set; }
    public DateTime? Issued { get; set; }
    public int SiteId { get; set; }
    public string? PropertyRef { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientContact { get; set; }
    public string? TaskRef { get; set; }
}

public class ReportListVm
{
    public ReportFilter Filter { get; set; } = new();
    public IReadOnlyList<ReportListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);
}
