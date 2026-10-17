using FyreApp.Models;

namespace FyreApp.Services.Properties;

public enum CreateSiteStatus
{
    Success = 1,
    NotFound = 2,
    ValidationError = 3,
    GeocodeFailed = 4,
    DuplicateName = 5,
    DuplicateAddress = 6
}

public sealed record CreateSiteResult(
    CreateSiteStatus Status,
    Property? site,
    string SiteName = "",
    string? Error = null
);