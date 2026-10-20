using System.Net;
using System.Text;
using FyreApp.Services.Uptick.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// The Uptick API client: read-only token, paging, the request budget, throttling and retries
public class UptickApiClientTests
{
    // A stand-in for Uptick: answers token and API requests, and records what was asked
    private sealed class FakeUptick : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Requests { get; } = [];
        public Func<HttpRequestMessage, string?, HttpResponseMessage>? Api { get; set; }
        public string TokenScope { get; set; } = "read";
        public int TokenCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.Parameter));

            if (request.RequestUri!.AbsolutePath == "/api/oauth2/token/")
            {
                TokenCalls++;
                return Json($$"""{"access_token":"token-{{TokenCalls}}","expires_in":36000,"token_type":"Bearer","scope":"{{TokenScope}}","refresh_token":"refresh-{{TokenCalls}}"}""");
            }
            return Api!(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Page(int from, int count, int total, bool next)
    {
        var records = Enumerable.Range(from, count).Select(i =>
            "{\"type\":\"Asset\",\"id\":\"" + i + "\",\"attributes\":{\"name\":\"A" + i + "\",\"updated\":\"2026-09-04T14:15:07+10:00\"}," +
            "\"relationships\":{\"property\":{\"data\":{\"type\":\"Property\",\"id\":\"7\"}}}}");
        return "{\"links\":{\"next\":" + (next ? "\"https://x/next\"" : "null") + "}," +
               "\"data\":[" + string.Join(",", records) + "]," +
               "\"meta\":{\"pagination\":{\"count\":" + total + ",\"limit\":" + count + ",\"offset\":" + from + "}}}";
    }

    // A clock the client's waits move forward, so pacing can be checked without sleeping
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (UptickApiClient Client, FakeUptick Uptick, List<TimeSpan> Waits, ManualClock Clock) Create(
        int perMinute = 6000, int pageSize = 3, int throttledPageSize = 2)
    {
        var uptick = new FakeUptick();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(uptick, disposeHandler: false));
        var options = Options.Create(new UptickApiOptions
        {
            BaseUrl = "https://fyrepower.onuptick.com/api/v2.15/",
            ClientId = "id", ClientSecret = "secret", Username = "api@fyrepower.com.au", Password = "pw",
            MaxRequestsPerMinute = perMinute, PageSize = pageSize, ThrottledPageSize = throttledPageSize
        });
        var waits = new List<TimeSpan>();
        var clock = new ManualClock();
        var client = new UptickApiClient(factory.Object, options, NullLogger<UptickApiClient>.Instance, clock, (d, _) =>
        {
            waits.Add(d);
            clock.Now += d;
            return Task.CompletedTask;
        });
        return (client, uptick, waits, clock);
    }

    private static async Task<List<UptickPage>> All(IAsyncEnumerable<UptickPage> pages)
    {
        var list = new List<UptickPage>();
        await foreach (var p in pages) list.Add(p);
        return list;
    }

    [Fact]
    public async Task Signs_in_with_a_read_only_password_grant()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (_, _) => Json("""{"assets":"https://fyrepower.onuptick.com/api/v2.15/assets/"}""");

        var endpoints = await client.ListEndpointsAsync();

        Assert.Equal(["assets"], endpoints);
        var token = uptick.Requests[0];
        Assert.Equal("https://fyrepower.onuptick.com/api/oauth2/token/", token.Url);
        Assert.Contains("grant_type=password", token.Body);
        Assert.Contains("scope=read", token.Body);
        Assert.Equal("token-1", uptick.Requests[1].Auth);
    }

    [Fact]
    public async Task Refuses_a_token_that_can_write()
    {
        var (client, uptick, _, _) = Create();
        uptick.TokenScope = "read write";
        uptick.Api = (_, _) => Json("{}");

        var ex = await Assert.ThrowsAsync<UptickApiException>(() => client.ListEndpointsAsync());
        Assert.Contains("read-only", ex.Message);
        Assert.Single(uptick.Requests); // the API itself was never called
    }

    [Fact]
    public async Task Only_reads()
    {
        var (client, _, _, _) = Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendAsync(HttpMethod.Post, new Uri("https://fyrepower.onuptick.com/api/v2.15/assets/"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendAsync(HttpMethod.Patch, new Uri("https://fyrepower.onuptick.com/api/v2.15/assets/1/"), default));
    }

    [Fact]
    public async Task Pages_through_an_endpoint_with_a_small_overlap()
    {
        var (client, uptick, _, _) = Create(pageSize: 100);
        var offsets = new List<string>();
        uptick.Api = (req, _) =>
        {
            var q = req.RequestUri!.Query;
            offsets.Add(q);
            return q.Contains("page%5Boffset%5D=0")
                ? Json(Page(1, 100, 130, next: true))
                : Json(Page(51, 80, 130, next: false));
        };

        var pages = await All(client.ListAsync("assets"));

        Assert.Equal(2, pages.Count);
        Assert.Equal(130, pages[0].Count);
        // The second page starts PageOverlap rows before the end of the first
        Assert.Contains($"page%5Boffset%5D={100 - UptickApiClient.PageOverlap}", offsets[1]);
        Assert.Contains("page%5Blimit%5D=100", offsets[0]);
        var first = pages[0].Records[0];
        Assert.Equal(("Asset", "1"), (first.Type, first.Id));
        Assert.Equal(new DateTime(2026, 9, 4, 4, 15, 7, DateTimeKind.Utc), first.UpdatedUtc);
        Assert.Contains("\"property\"", first.Relationships);
    }

    [Fact]
    public async Task Reads_endpoints_that_return_plain_rows_under_results()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (_, _) => Json("""
            {"results":[{"task_id":"46459","updated":"2026-10-16T03:06:48+11:00","billable":"-96.0000"}],
             "meta":{"pagination":{"count":1}},"links":{"next":null}}
            """);

        var record = (await All(client.ListAsync("intelligencereports/profitability_by_task"))).Single().Records.Single();

        Assert.Equal("46459", record.Id);
        Assert.Contains("billable", record.Attributes);
        Assert.Equal(new DateTime(2026, 10, 15, 16, 6, 48, DateTimeKind.Utc), record.UpdatedUtc);
    }

    [Fact]
    public async Task Asks_for_changes_since_a_time()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (_, _) => Json(Page(1, 1, 1, next: false));

        await All(client.ListAsync("assets", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("updatedsince=2026-10-01T00%3A00%3A00Z", uptick.Requests[^1].Url);
    }

    [Fact]
    public async Task Keeps_to_the_requests_per_minute_budget()
    {
        var (client, uptick, waits, _) = Create(perMinute: 60);
        uptick.Api = (_, _) => Json("{}");

        for (var i = 0; i < 4; i++) await client.ListEndpointsAsync();

        // 1 token + 4 API requests: every start after the first waits its 1-second slot
        Assert.Equal(5, client.Requests);
        Assert.Equal(4, waits.Count);
        Assert.All(waits, w => Assert.Equal(TimeSpan.FromSeconds(1), w));
    }

    [Fact]
    public async Task Waits_as_Uptick_asks_when_throttled_then_uses_smaller_pages()
    {
        var (client, uptick, waits, _) = Create(pageSize: 3, throttledPageSize: 2);
        var calls = 0;
        var queries = new List<string>();
        uptick.Api = (req, _) =>
        {
            queries.Add(req.RequestUri!.Query);
            if (++calls == 1)
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(14));
                return r;
            }
            return Json(Page(1, 1, 1, next: false));
        };

        await All(client.ListAsync("assets"));

        Assert.Contains(TimeSpan.FromSeconds(14), waits);
        Assert.Equal(1, client.Throttled);
        Assert.Contains("page%5Blimit%5D=3", queries[0]);
        // The retry was built before throttling; the next page asked for would be smaller
        await All(client.ListAsync("assets"));
        Assert.Contains("page%5Blimit%5D=2", queries[^1]);
    }

    [Fact]
    public async Task Stops_when_Uptick_asks_to_wait_half_an_hour_or_more()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(30));
            return r;
        };

        await Assert.ThrowsAsync<UptickRateLimitedException>(() => client.ListEndpointsAsync());
    }

    [Fact]
    public async Task Gives_up_after_five_retries()
    {
        var (client, uptick, waits, _) = Create();
        uptick.Api = (_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<UptickApiException>(() => client.ListEndpointsAsync());

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(1 + UptickApiClient.MaxRetries, uptick.Requests.Count(r => r.Url.Contains("/v2.15/")));
        Assert.Equal([2, 4, 8, 16, 32], waits.Select(w => w.TotalSeconds).Where(s => s > 1));
    }

    [Fact]
    public async Task Signs_in_again_once_when_the_token_is_refused()
    {
        var (client, uptick, _, _) = Create();
        var calls = 0;
        uptick.Api = (_, _) => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json("{}");

        await client.ListEndpointsAsync();

        Assert.Equal(2, uptick.TokenCalls);
        Assert.Equal("token-2", uptick.Requests[^1].Auth);
    }

    [Fact]
    public async Task Reports_a_forbidden_endpoint_with_its_status()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<UptickApiException>(() => All(client.ListAsync("logins")));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task A_document_download_gives_the_link_it_redirects_to_without_following_it()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/v2.15/documents/b717/download/" => new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://uptick-customer-storage.s3.amazonaws.com/fyrepower/tasks/1/a.png?X-Amz-Signature=x") }
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        Assert.Equal("https://uptick-customer-storage.s3.amazonaws.com/fyrepower/tasks/1/a.png?X-Amz-Signature=x",
            (await client.DocumentDownloadUrlAsync("b717"))!.ToString());
        Assert.Null(await client.DocumentDownloadUrlAsync("gone"));
        // Only the API was asked: the storage link is the caller's to fetch, without the token
        Assert.DoesNotContain(uptick.Requests, r => r.Url.Contains("amazonaws"));
        Assert.All(uptick.Requests, r => Assert.Contains(r.Method, new[] { HttpMethod.Get, HttpMethod.Post }));
    }

    [Fact]
    public async Task A_records_pdf_is_read_with_its_link_or_none_when_uptick_has_not_made_one()
    {
        var (client, uptick, _, _) = Create();
        uptick.Api = (req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/v2.15/reports/9999/pdf/" => Json("""{"key":"fyrepower/reports/9999/R-09999 P-0209 Service Report.pdf","size":88796,"modified":"2024-01-17T04:06:33+00:00","metadata":{},"url":"https://uptick-customer-storage.s3.amazonaws.com/fyrepower/reports/9999/R.pdf?sig=x"}"""),
            "/api/v2.15/reports/10/pdf/" => Json(""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        var pdf = await client.PdfAsync("reports", "9999");
        Assert.Equal(("fyrepower/reports/9999/R-09999 P-0209 Service Report.pdf", 88796L, new DateTime(2024, 1, 17, 4, 6, 33, DateTimeKind.Utc)),
            (pdf!.Key, pdf.Size!.Value, pdf.ModifiedUtc!.Value));
        Assert.Equal("uptick-customer-storage.s3.amazonaws.com", pdf.Url.Host);
        Assert.Null(await client.PdfAsync("reports", "10"));
        Assert.Null(await client.PdfAsync("reports", "11"));
        // Read only: never the POST that would make Uptick generate one
        Assert.DoesNotContain(uptick.Requests, r => r.Method == HttpMethod.Post && r.Url.Contains("/pdf/"));
    }
}
