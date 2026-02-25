namespace FyreApp.Services.Sites;

public enum CreateSiteStatus
{
    Success = 1,
    NotFound = 2,
    ValidationError = 3,
    GeocodeFailed = 4
}

public sealed record CreateSiteResult(
    CreateSiteStatus Status,
    int? SiteId = null,
    string? Error = null
);