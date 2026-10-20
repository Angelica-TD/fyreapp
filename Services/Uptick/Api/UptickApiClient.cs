using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FyreApp.Services.Uptick.Api;

// Reads Uptick's REST API (docs/uptick-migration.md §3.1). Read-only by construction: the token is requested with
// scope=read, and nothing here sends a request that could change Uptick (only GET and OPTIONS reach the API).
public interface IUptickApiClient
{
    bool IsConfigured { get; }
    string BaseUrl { get; }
    string Username { get; }

    // Requests sent and times Uptick throttled, since the last ResetForRun
    int Requests { get; }
    int Throttled { get; }

    // Back to the configured pace and page size, and counters to zero, at the start of a run
    void ResetForRun();

    // The endpoints the API root lists, e.g. "assets"
    Task<IReadOnlyList<string>> ListEndpointsAsync(CancellationToken ct = default);

    // Every record of an endpoint (changed since a time, and matching a filter such as invoice=129, when given),
    // a page at a time. With fields, only those of each record (fields[AssetType]=applicable_routines): the way to
    // get to-many relationships, which list responses leave out.
    IAsyncEnumerable<UptickPage> ListAsync(string endpoint, DateTime? updatedSinceUtc = null, (string Field, string Value)? filter = null,
        (string Type, string Names)? fields = null, CancellationToken ct = default);
}

public class UptickApiClient : IUptickApiClient
{
    public const string HttpClientName = "uptick";

    // Each page starts this many rows before where the last one ended, so a record deleted in Uptick mid-run
    // (which shifts the rest up) can't make the next one be skipped and then wrongly marked deleted
    internal const int PageOverlap = 50;

    // Waits Uptick asks for at or above this stop the run instead (as Airbyte's connector does)
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(30);
    internal const int MaxRetries = 5;

    private readonly IHttpClientFactory _httpFactory;
    private readonly UptickApiOptions _options;
    private readonly ILogger<UptickApiClient> _logger;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _paceLock = new(1, 1);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private DateTime _nextSlotUtc = DateTime.MinValue;
    private double _requestsPerMinute;
    private int _pageSize;
    private int _requests, _throttled;

    private string? _accessToken, _refreshToken;
    private DateTime _tokenExpiresUtc;

    public UptickApiClient(IHttpClientFactory httpFactory, IOptions<UptickApiOptions> options, ILogger<UptickApiClient> logger,
        TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _delay = delay ?? ((d, ct) => Task.Delay(d, _clock, ct));
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
        ResetForRun();
    }

    public bool IsConfigured => _options.IsConfigured;
    public string BaseUrl => Root.ToString();
    public string Username => _options.Username;
    public int Requests => _requests;
    public int Throttled => _throttled;

    private Uri Root => new(_options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/");

    public void ResetForRun()
    {
        _requestsPerMinute = Math.Max(1, _options.MaxRequestsPerMinute);
        _pageSize = Math.Max(1, _options.PageSize);
        _requests = _throttled = 0;
    }

    public async Task<IReadOnlyList<string>> ListEndpointsAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await GetAsync(Root, ct));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }

    public async IAsyncEnumerable<UptickPage> ListAsync(string endpoint, DateTime? updatedSinceUtc = null,
        (string Field, string Value)? filter = null, (string Type, string Names)? fields = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var offset = 0;
        while (true)
        {
            var limit = _pageSize;
            var query = $"page%5Blimit%5D={limit}&page%5Boffset%5D={offset}";
            if (updatedSinceUtc is { } since)
                query += "&updatedsince=" + Uri.EscapeDataString(since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            if (filter is { } f)
                query += $"&{Uri.EscapeDataString(f.Field)}={Uri.EscapeDataString(f.Value)}";
            if (fields is { } only)
                query += $"&fields%5B{Uri.EscapeDataString(only.Type)}%5D={Uri.EscapeDataString(only.Names)}";

            using var doc = JsonDocument.Parse(await GetAsync(new Uri(Root, $"{endpoint.Trim('/')}/?{query}"), ct));
            var root = doc.RootElement;

            // JSON:API "data", or "results" on the few endpoints that return plain rows
            var records = (root.TryGetProperty("data", out var data) || root.TryGetProperty("results", out data))
                          && data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray().Select(UptickResource.FromJson).ToList()
                : [];
            int? count = root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("pagination", out var p)
                         && p.TryGetProperty("count", out var c) && c.TryGetInt32(out var n)
                ? n
                : null;
            var hasNext = root.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var next)
                          && next.ValueKind == JsonValueKind.String;

            yield return new UptickPage(count, records);

            if (!hasNext || records.Count == 0) yield break;
            offset += records.Count > PageOverlap ? records.Count - PageOverlap : records.Count;
        }
    }

    private Task<string> GetAsync(Uri uri, CancellationToken ct) => SendAsync(HttpMethod.Get, uri, ct);

    // The only way a request reaches Uptick's data. GET and OPTIONS only: FyreApp never changes Uptick.
    internal async Task<string> SendAsync(HttpMethod method, Uri uri, CancellationToken ct)
    {
        if (method != HttpMethod.Get && method != HttpMethod.Options)
            throw new InvalidOperationException("FyreApp only reads from Uptick.");
        if (!IsConfigured)
            throw new UptickApiException(0, "The Uptick API isn't configured (Uptick:Api settings).");

        var retries = 0;
        var reauthenticated = false;
        while (true)
        {
            var token = await GetTokenAsync(ct);
            HttpResponseMessage? response = null;
            string? body = null;
            await AcquireAsync(ct);
            try
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // No Accept header: the API root answers 406 to "application/vnd.api+json", and without one every
                // endpoint returns JSON:API anyway
                response = await Http().SendAsync(request, ct);
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                // Network error or timeout: back off and try again
                if (++retries > MaxRetries) throw new UptickApiException(0, $"Uptick didn't answer: {ex.Message}");
                _logger.LogWarning("Uptick request failed ({Error}); retry {Retry}", ex.Message, retries);
                await _delay(Backoff(retries), ct);
                continue;
            }
            finally
            {
                _concurrency.Release();
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode) return body!;

                if (response.StatusCode == HttpStatusCode.Unauthorized && !reauthenticated)
                {
                    // Token expired or was revoked: sign in again once
                    reauthenticated = true;
                    _accessToken = null;
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Interlocked.Increment(ref _throttled);
                    var retryAfter = RetryAfter(response);
                    if (retryAfter >= MaxRetryAfter) throw new UptickRateLimitedException(retryAfter.Value);

                    // Slower and lighter for the rest of the run
                    _requestsPerMinute = Math.Max(6, _requestsPerMinute / 2);
                    _pageSize = Math.Min(_pageSize, Math.Max(1, _options.ThrottledPageSize));

                    if (++retries > MaxRetries) throw new UptickApiException(status, "Uptick kept throttling requests.");
                    _logger.LogWarning("Uptick throttled a request; waiting {Wait}", retryAfter ?? Backoff(retries));
                    await _delay(retryAfter ?? Backoff(retries), ct);
                    continue;
                }

                if (status >= 500)
                {
                    if (++retries > MaxRetries) throw new UptickApiException(status, $"Uptick returned {status}.");
                    await _delay(Backoff(retries), ct);
                    continue;
                }

                throw new UptickApiException(status, $"Uptick returned {status} for {uri.AbsolutePath}.");
            }
        }
    }

    // One request slot: at most MaxConcurrency in flight, starts spaced to stay under the per-minute budget.
    // The caller releases _concurrency when the response is read.
    private async Task AcquireAsync(CancellationToken ct)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            await _paceLock.WaitAsync(ct);
            try
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                if (_nextSlotUtc > now) await _delay(_nextSlotUtc - now, ct);
                var start = _nextSlotUtc > now ? _nextSlotUtc : now;
                _nextSlotUtc = start + TimeSpan.FromMinutes(1 / _requestsPerMinute);
            }
            finally
            {
                _paceLock.Release();
            }
        }
        catch
        {
            _concurrency.Release();
            throw;
        }
        Interlocked.Increment(ref _requests);
    }

    private static TimeSpan Backoff(int retry) => TimeSpan.FromSeconds(Math.Pow(2, retry));

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - _clock.GetUtcNow() is var d && d > TimeSpan.Zero ? d : TimeSpan.Zero;
        return null;
    }

    private HttpClient Http()
    {
        var http = _httpFactory.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds));
        return http;
    }

    // OAuth2 password grant (the only one Uptick allows this client), always scope=read. Refreshed before it
    // expires; a failed refresh falls back to signing in again. Tokens are never logged.
    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && _tokenExpiresUtc > _clock.GetUtcNow().UtcDateTime.AddMinutes(5)) return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && _tokenExpiresUtc > _clock.GetUtcNow().UtcDateTime.AddMinutes(5)) return _accessToken;

            if (_refreshToken != null && await RequestTokenAsync(new()
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = _refreshToken
                }, throwOnRefusal: false, ct))
                return _accessToken!;

            await RequestTokenAsync(new()
            {
                ["grant_type"] = "password", ["username"] = _options.Username, ["password"] = _options.Password
            }, throwOnRefusal: true, ct);
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<bool> RequestTokenAsync(Dictionary<string, string> form, bool throwOnRefusal, CancellationToken ct)
    {
        form["client_id"] = _options.ClientId;
        form["client_secret"] = _options.ClientSecret;
        form["scope"] = "read";

        await AcquireAsync(ct);
        HttpResponseMessage response;
        string body;
        try
        {
            response = await Http().PostAsync(new Uri(Root, "/api/oauth2/token/"), new FormUrlEncodedContent(form), ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        finally
        {
            _concurrency.Release();
        }

        using (response)
        {
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(body); } catch (JsonException) { }
            using var _ = doc;
            var json = doc?.RootElement ?? default;

            if (!response.IsSuccessStatusCode || json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("access_token", out var access))
            {
                _refreshToken = null;
                if (!throwOnRefusal) return false;
                var error = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var e) ? e.GetString() : null;
                throw new UptickApiException((int)response.StatusCode,
                    $"Uptick refused the API login ({error ?? ((int)response.StatusCode).ToString()}). Check the Uptick:Api client ID, secret, username and password.");
            }

            // A token that can write would break the promise that FyreApp never changes Uptick
            if (json.TryGetProperty("scope", out var scope) && scope.GetString()?.Split(' ').Contains("write") == true)
                throw new UptickApiException(0, "Uptick granted a token that can write; FyreApp only accepts read-only access.");

            _accessToken = access.GetString();
            _refreshToken = json.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
            var seconds = json.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var s) ? s : 3600;
            _tokenExpiresUtc = _clock.GetUtcNow().UtcDateTime.AddSeconds(seconds);
            return true;
        }
    }
}
