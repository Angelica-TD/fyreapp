namespace FyreApp.ViewModels.Dev;

public sealed class DevIndexVm
{
    public List<DevClientRow> Clients { get; set; } = [];
    public List<DevSiteRow> Sites { get; set; } = [];
    public List<DevAssetRow> Assets { get; set; } = [];
    public List<DevTaskRow> Tasks { get; set; } = [];
    public List<DevCatalogueRow> Catalogue { get; set; } = [];
}

public sealed record DevCatalogueRow(int Id, string Name);
public sealed record DevClientRow(int Id, string Name, int SiteCount, int TaskCount);
public sealed record DevSiteRow(int Id, string Name, string ClientName, string? Address, int AssetCount);
public sealed record DevAssetRow(int Id, string Name, string SiteName, string ClientName);
public sealed record DevTaskRow(int Id, string Title, string ClientName, string? SiteName, string Status);
