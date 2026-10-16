namespace FyreApp.Services.Uptick.Api;

// Configuration section "Uptick:Api". The client ID, secret, username and password are secrets: user secrets in
// development, /etc/fyreapp.env in production (Uptick__Api__ClientId, ...). Never in appsettings*.json.
public sealed class UptickApiOptions
{
    // Includes the pinned API version (docs/uptick-migration.md §1)
    public string BaseUrl { get; set; } = "https://fyrepower.onuptick.com/api/v2.15/";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Uptick can revoke API access without notice for going over its (unpublished) limits
    public int MaxRequestsPerMinute { get; set; } = 60;
    public int MaxConcurrency { get; set; } = 2;

    // Rows per request; drops to ThrottledPageSize for the rest of a run once Uptick throttles
    public int PageSize { get; set; } = 5000;
    public int ThrottledPageSize { get; set; } = 1000;

    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
