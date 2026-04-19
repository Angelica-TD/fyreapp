using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.Dev;

public sealed class DevIndexVm
{
    public List<DevClientRow> Clients { get; set; } = [];
    public List<DevSiteRow> Sites { get; set; } = [];
    public List<DevAssetRow> Assets { get; set; } = [];
    public List<DevTaskRow> Tasks { get; set; } = [];
    public List<DevCatalogueRow> Catalogue { get; set; } = [];
    public List<DevTechRow> Techs { get; set; } = [];
    public List<DevTechRow> Admins { get; set; } = [];
}

public sealed record DevCatalogueRow(int Id, string Name);
public sealed record DevClientRow(int Id, string Name, int SiteCount, int TaskCount);
public sealed record DevSiteRow(int Id, string Name, string ClientName, string? Address, int AssetCount);
public sealed record DevAssetRow(int Id, string Name, string SiteName, string ClientName);
public sealed record DevTaskRow(int Id, string Title, string ClientName, string? SiteName, string Status);
public sealed record DevTechRow(string Id, string FullName, string Email, bool IsActive);

public sealed class DevClientUpdateVm
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    public string? ExternalId { get; set; }
    public bool Active { get; set; }
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? PrimaryContactMobile { get; set; }
    public string? PrimaryContactCcEmail { get; set; }
    public string? PrimaryContactAddress { get; set; }
    public string? BillingName { get; set; }
    public string? BillingAttentionTo { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingCcEmail { get; set; }
    public string? BillingAddress { get; set; }
}

public sealed class DevSiteUpdateVm
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    public string? AddressDisplay { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Suburb { get; set; }
    public string? Postcode { get; set; }
    public string? State { get; set; }
}

public sealed class DevAssetUpdateVm
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
}

public sealed class DevTaskUpdateVm
{
    public int Id { get; set; }
    [Required] public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ClientTaskStatus Status { get; set; }
    public ClientTaskPriority Priority { get; set; }
    public DateTime? DueDateUtc { get; set; }
}

public sealed class DevUserUpdateVm
{
    public string Id { get; set; } = "";
    [Required] public string FirstName { get; set; } = "";
    [Required] public string LastName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public string? NewPassword { get; set; }
}
