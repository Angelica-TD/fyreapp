namespace FyreApp.ViewModels.Assets;

// Filters for the Assets page. Defaults match Uptick's Assets list: active assets at properties that
// aren't inactive (Active, Setup, On hold), any asset type.
public class AssetFilter
{
    public string? Search { get; set; }

    // true = active assets only, false = inactive only, null = any
    public bool? Active { get; set; } = true;

    // Empty = any asset type; with AssetTypeIsNot, every type except these
    public List<int> AssetTypeIds { get; set; } = new();
    public bool AssetTypeIsNot { get; set; }

    // Empty = any
    public List<string> PropertyStatus { get; set; } = FyreApp.ViewModels.Lists.ListFilters.NotInactive();

    public int Page { get; set; } = 1;

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FyreApp.ViewModels.Lists.FilterQuery.Kv("f", "true");
        yield return FyreApp.ViewModels.Lists.FilterQuery.Kv("search", Search);
        yield return FyreApp.ViewModels.Lists.FilterQuery.Kv("active", FyreApp.ViewModels.Lists.FilterQuery.YesNoAny(Active));
        yield return FyreApp.ViewModels.Lists.FilterQuery.Kv("assetTypeIsNot", AssetTypeIsNot ? "true" : "false");
        foreach (var id in AssetTypeIds) yield return FyreApp.ViewModels.Lists.FilterQuery.Kv("assetType", id.ToString());
        foreach (var s in PropertyStatus) yield return FyreApp.ViewModels.Lists.FilterQuery.Kv("propertyStatus", s);
    }

    // How many filters narrow the list (shown on the Filters button, as Uptick does)
    public int ActiveCount =>
        (Active != null ? 1 : 0) +
        (AssetTypeIds.Count > 0 ? 1 : 0) +
        (PropertyStatus.Count > 0 ? 1 : 0);
}

public class AssetListItemVm
{
    public int Id { get; set; }
    public string? Ref { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SiteId { get; set; }
    public string? PropertyRef { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientContact { get; set; }
}

public class AssetListVm
{
    public const int PageSize = 50;

    public AssetFilter Filter { get; set; } = new();
    public IReadOnlyList<AssetListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    // Options for the asset type filter
    public IReadOnlyList<(int Id, string Name)> AssetTypes { get; set; } = [];
}
