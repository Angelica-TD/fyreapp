namespace FyreApp.ViewModels.Lists;

public enum RemarkCompliance
{
    Open = 1,      // not resolved (Uptick's remark "Active")
    Resolved = 2
}

// Defaults match Uptick's Remarks list: open defects (non-conformance, non-critical, critical) on active
// assets at properties that aren't inactive
public class RemarkFilter
{
    public static readonly string[] DefaultSeverities = { "Non-conformance", "Non-critical defect", "Critical defect" };

    public string? Search { get; set; }

    // true = active assets only, false = inactive assets only, null = any (including remarks with no asset)
    public bool? AssetActive { get; set; } = true;

    public List<RemarkCompliance> Compliance { get; set; } = new() { RemarkCompliance.Open };
    public List<string> PropertyStatus { get; set; } = ListFilters.NotInactive();
    public List<string> Severity { get; set; } = DefaultSeverities.ToList();
    public int Page { get; set; } = 1;

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("f", "true");
        yield return FilterQuery.Kv("search", Search);
        yield return FilterQuery.Kv("assetActive", FilterQuery.YesNoAny(AssetActive));
        foreach (var c in Compliance) yield return FilterQuery.Kv("compliance", c.ToString());
        foreach (var s in PropertyStatus) yield return FilterQuery.Kv("propertyStatus", s);
        foreach (var s in Severity) yield return FilterQuery.Kv("severity", s);
    }

    public int ActiveCount =>
        (AssetActive != null ? 1 : 0) +
        (Compliance.Count > 0 ? 1 : 0) +
        (PropertyStatus.Count > 0 ? 1 : 0) +
        (Severity.Count > 0 ? 1 : 0);
}

public class RemarkListItemVm
{
    public int Id { get; set; }
    public string? Ref { get; set; }
    public string? Severity { get; set; }
    public string? RemarkType { get; set; }
    public int? AssetId { get; set; }
    public string? AssetName { get; set; }
    public int SiteId { get; set; }
    public string? PropertyRef { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string? QuoteStatus { get; set; }
    public string? QuoteRef { get; set; }
}

public class RemarkListVm
{
    public RemarkFilter Filter { get; set; } = new();
    public IReadOnlyList<RemarkListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);

    // Options for the severity filter (labels in use)
    public IReadOnlyList<string> Severities { get; set; } = [];
}
