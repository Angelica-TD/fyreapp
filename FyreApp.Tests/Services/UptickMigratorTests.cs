using System.Runtime.CompilerServices;
using System.Text.Json;
using FyreApp.Data;
using FyreApp.Services.Uptick.Api;
using FyreApp.Services.Uptick.Migration;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FyreApp.Tests.Services;

// Copying Uptick's API into UptickRecord: new, changed, deleted and restored records, the updatedsince check,
// Refresh runs and the run report (docs/uptick-migration.md §4)
public class UptickMigratorTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    // Uptick's API in memory: endpoints with records, which can honour updatedsince or ignore it
    private sealed class FakeApi : IUptickApiClient
    {
        public Dictionary<string, List<UptickResource>> Endpoints { get; } = new();
        public HashSet<string> IgnoresUpdatedSince { get; } = [];
        public Dictionary<string, int> Fail { get; } = new();
        // Uptick's count is higher than what it returns (records lost in paging)
        public Dictionary<string, int> ReportedCount { get; } = new();
        public List<(string Endpoint, DateTime? Since)> Calls { get; } = [];
        public int PageSize { get; set; } = 2;
        public Action? OnPage { get; set; }

        public bool IsConfigured => true;
        public string BaseUrl => "https://fyrepower.onuptick.com/api/v2.15/";
        public string Username => "api";
        public int Requests { get; private set; }
        public int Throttled => 0;
        public void ResetForRun() => Requests = 0;

        public Task<IReadOnlyList<string>> ListEndpointsAsync(CancellationToken ct = default)
        {
            Requests++;
            return Task.FromResult<IReadOnlyList<string>>(Endpoints.Keys.ToList());
        }

        public async IAsyncEnumerable<UptickPage> ListAsync(string endpoint, DateTime? updatedSinceUtc = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls.Add((endpoint, updatedSinceUtc));
            if (Fail.TryGetValue(endpoint, out var status)) throw new UptickApiException(status, $"Uptick returned {status}.");

            var records = Endpoints[endpoint]
                .Where(r => updatedSinceUtc == null || IgnoresUpdatedSince.Contains(endpoint) || r.UpdatedUtc >= updatedSinceUtc)
                .ToList();
            var count = ReportedCount.GetValueOrDefault(endpoint, records.Count);
            if (records.Count == 0) { Requests++; yield return new UptickPage(count, []); yield break; }
            foreach (var chunk in records.Chunk(PageSize))
            {
                ct.ThrowIfCancellationRequested();
                Requests++;
                OnPage?.Invoke();
                await Task.Yield();
                yield return new UptickPage(count, chunk);
            }
        }
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private static UptickResource Asset(int id, string name = "Extinguisher", int daysAgo = 30) =>
        new("Asset", id.ToString(), JsonSerializer.Serialize(new { name, location = "Kitchen" }),
            """{"property":{"data":{"type":"Property","id":"7"}}}""", Now.AddDays(-400), Now.AddDays(-daysAgo));

    private static (UptickMigrator Migrator, FixedClock Clock) Migrator(AppDbContext db, FakeApi api)
    {
        var clock = new FixedClock(Now);
        return (new UptickMigrator(db, api, NullLogger<UptickMigrator>.Instance, clock), clock);
    }

    private static List<UptickEndpointResult> Report(FyreApp.Models.UptickMigrationRun run) =>
        JsonSerializer.Deserialize<List<UptickEndpointResult>>(run.Report!)!;

    [Fact]
    public async Task A_full_run_copies_every_record_and_reports_it()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1), Asset(2), Asset(3)];
        api.Endpoints["clients"] = [];

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, "admin");

        Assert.Equal("Completed", run.Status);
        Assert.Equal(3, await db.UptickRecords.CountAsync(r => r.Resource == "assets"));
        var row = await db.UptickRecords.SingleAsync(r => r.Resource == "assets" && r.UptickId == "2");
        Assert.Equal("Asset", row.Type);
        Assert.Contains("Kitchen", row.Attributes);
        Assert.Null(row.MappedUtc);

        var report = Report(run);
        var assets = report.Single(r => r.Resource == "assets");
        Assert.Equal(("Ok", 3, 3, 3, 3), (assets.Status, assets.UptickCount, assets.Received, assets.Created, assets.Copied));
        Assert.Equal("Empty", report.Single(r => r.Resource == "clients").Status);

        var state = await db.UptickEndpointStates.SingleAsync(s => s.Resource == "assets");
        Assert.Equal((3, 3, "Ok"), (state.UptickCount, state.CopiedCount, state.Status));
        Assert.Equal(Now, state.LastFullRunUtc);
        Assert.Equal(Now.AddDays(-30), state.CursorUtc);
    }

    [Fact]
    public async Task Running_again_updates_changed_records_and_leaves_the_rest()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1), Asset(2)];
        var (migrator, clock) = Migrator(db, api);
        await migrator.RunAsync(UptickRunKind.Full, null, null);

        // Mapped since; then asset 2 changes in Uptick
        foreach (var r in db.UptickRecords) r.MappedUtc = Now;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        api.Endpoints["assets"] = [Asset(1), Asset(2, "Hose reel", daysAgo: 1)];
        clock.Now = Now.AddHours(1);

        var run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        var assets = Report(run).Single();
        Assert.Equal((0, 1, 1), (assets.Created, assets.Updated, assets.Unchanged));
        var changed = await db.UptickRecords.SingleAsync(r => r.UptickId == "2");
        Assert.Contains("Hose reel", changed.Attributes);
        Assert.Null(changed.MappedUtc); // to be mapped again
        Assert.NotNull((await db.UptickRecords.SingleAsync(r => r.UptickId == "1")).MappedUtc);
    }

    [Fact]
    public async Task Records_gone_from_Uptick_are_marked_deleted_and_restored_if_they_return()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = Enumerable.Range(1, 20).Select(i => Asset(i)).ToList();
        var (migrator, clock) = Migrator(db, api);
        await migrator.RunAsync(UptickRunKind.Full, null, null);

        api.Endpoints["assets"].RemoveAll(a => a.Id == "5");
        clock.Now = Now.AddHours(1);
        var run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        Assert.Equal(1, Report(run).Single().Deleted);
        var gone = await db.UptickRecords.SingleAsync(r => r.UptickId == "5");
        Assert.Equal(Now.AddHours(1), gone.DeletedUtc);
        Assert.Equal(20, await db.UptickRecords.CountAsync()); // never removed
        Assert.Equal(19, (await db.UptickEndpointStates.SingleAsync()).CopiedCount);

        api.Endpoints["assets"].Add(Asset(5));
        clock.Now = Now.AddHours(2);
        run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        Assert.Equal(1, Report(run).Single().Restored);
        Assert.Null((await db.UptickRecords.SingleAsync(r => r.UptickId == "5")).DeletedUtc);
    }

    [Fact]
    public async Task Deletions_are_not_marked_when_Uptick_returned_less_than_it_reported()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1), Asset(2), Asset(3)];
        var (migrator, clock) = Migrator(db, api);
        await migrator.RunAsync(UptickRunKind.Full, null, null);

        api.Endpoints["assets"] = [Asset(1), Asset(2)];
        api.ReportedCount["assets"] = 3;
        clock.Now = Now.AddHours(1);
        var run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        var result = Report(run).Single();
        Assert.Equal(0, result.Deleted);
        Assert.Contains("deletions not checked", result.Warning);
        Assert.Null((await db.UptickRecords.SingleAsync(r => r.UptickId == "3")).DeletedUtc);
    }

    [Fact]
    public async Task An_endpoint_that_returns_fewer_records_than_it_counts_is_incomplete()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        // invoicelineitems: Uptick's paging skips records it counts
        api.Endpoints["invoicelineitems"] = [Asset(1, daysAgo: 2), Asset(2)];
        api.ReportedCount["invoicelineitems"] = 3;

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null);

        var result = Report(run).Single();
        Assert.Equal(("Incomplete", 3, 2), (result.Status, result.UptickCount, result.Received));
        Assert.Null(result.IncrementalVerified); // can't be checked against an incomplete pull
        Assert.Equal("Incomplete", (await db.UptickEndpointStates.SingleAsync()).Status);
    }

    [Fact]
    public async Task Deletions_are_not_marked_when_far_fewer_records_came_back()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = Enumerable.Range(1, 10).Select(i => Asset(i)).ToList();
        var (migrator, clock) = Migrator(db, api);
        await migrator.RunAsync(UptickRunKind.Full, null, null);

        api.Endpoints["assets"] = Enumerable.Range(1, 5).Select(i => Asset(i)).ToList();
        clock.Now = Now.AddHours(1);
        var run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        Assert.Equal(0, Report(run).Single().Deleted);
        Assert.Equal(10, (await db.UptickEndpointStates.SingleAsync()).CopiedCount);
    }

    [Fact]
    public async Task Excluded_endpoints_are_listed_but_not_fetched()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1)];
        api.Endpoints["userpreferences"] = [Asset(9)];

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null);

        Assert.DoesNotContain(api.Calls, c => c.Endpoint == "userpreferences");
        Assert.Equal("Excluded", Report(run).Single(r => r.Resource == "userpreferences").Status);
        Assert.False((await db.UptickEndpointStates.SingleAsync(s => s.Resource == "userpreferences")).Enabled);
    }

    [Fact]
    public async Task An_endpoint_the_login_cannot_see_is_reported_and_the_run_carries_on()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1)];
        api.Endpoints["customsqlreports"] = [];
        api.Endpoints["tasks"] = [Asset(2)];
        api.Fail["customsqlreports"] = 403;
        api.Fail["tasks"] = 500;

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null);

        Assert.Equal("Completed", run.Status);
        var report = Report(run);
        Assert.Equal("Forbidden", report.Single(r => r.Resource == "customsqlreports").Status);
        Assert.Equal("Failed", report.Single(r => r.Resource == "tasks").Status);
        Assert.Equal("Ok", report.Single(r => r.Resource == "assets").Status);
        Assert.Equal("Uptick returned 500.", (await db.UptickEndpointStates.SingleAsync(s => s.Resource == "tasks")).LastError);
    }

    [Fact]
    public async Task A_full_run_checks_whether_updatedsince_can_be_trusted()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        // Some, not all, changed in the last 7 days
        api.Endpoints["assets"] = [Asset(1, daysAgo: 2), Asset(2, daysAgo: 30), Asset(3, daysAgo: 60)];
        api.Endpoints["contacts"] = [Asset(1, daysAgo: 2), Asset(2, daysAgo: 30), Asset(3, daysAgo: 60)];
        api.IgnoresUpdatedSince.Add("contacts");

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null);

        var report = Report(run);
        Assert.True(report.Single(r => r.Resource == "assets").IncrementalVerified);
        Assert.False(report.Single(r => r.Resource == "contacts").IncrementalVerified);
        Assert.True((await db.UptickEndpointStates.SingleAsync(s => s.Resource == "assets")).IncrementalVerified);
        Assert.False((await db.UptickEndpointStates.SingleAsync(s => s.Resource == "contacts")).IncrementalVerified);
        Assert.Contains(api.Calls, c => c.Endpoint == "assets" && c.Since == Now - UptickMigrator.VerifyWindow);
    }

    [Fact]
    public async Task The_check_is_skipped_when_it_cannot_tell()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assettypes"] = [Asset(1, daysAgo: 300), Asset(2, daysAgo: 300)];

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null);

        Assert.Contains("Not checked", Report(run).Single().IncrementalNote);
        Assert.Single(api.Calls); // no extra request
    }

    [Fact]
    public async Task A_refresh_asks_for_changes_only_where_updatedsince_is_trusted()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1, daysAgo: 2), Asset(2, daysAgo: 30), Asset(3, daysAgo: 60)];
        api.Endpoints["contacts"] = [Asset(1, daysAgo: 2), Asset(2, daysAgo: 30), Asset(3, daysAgo: 60)];
        api.IgnoresUpdatedSince.Add("contacts");
        var (migrator, clock) = Migrator(db, api);
        await migrator.RunAsync(UptickRunKind.Full, null, null);

        api.Endpoints["assets"].Add(Asset(4, "New", daysAgo: 0));
        clock.Now = Now.AddHours(1);
        api.Calls.Clear();
        var run = await migrator.RunAsync(UptickRunKind.Refresh, null, null);

        var report = Report(run);
        var assets = report.Single(r => r.Resource == "assets");
        Assert.Equal(("Changes", 1), (assets.Mode, assets.Created));
        Assert.Equal(("assets", (DateTime?)(Now.AddDays(-2) - UptickMigrator.RefreshOverlap)), api.Calls.Single(c => c.Endpoint == "assets"));
        Assert.Equal("Full", report.Single(r => r.Resource == "contacts").Mode);
        Assert.Null(api.Calls.Single(c => c.Endpoint == "contacts").Since);
        Assert.Equal(4, (await db.UptickEndpointStates.SingleAsync(s => s.Resource == "assets")).CopiedCount);
        Assert.Equal(Now.AddHours(1), (await db.UptickEndpointStates.SingleAsync(s => s.Resource == "assets")).LastRefreshUtc);
    }

    [Fact]
    public async Task A_single_endpoint_can_be_run_on_its_own()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1)];
        api.Endpoints["tasks"] = [Asset(2)];

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, "tasks", null);

        Assert.Equal("tasks", Report(run).Single().Resource);
        Assert.DoesNotContain(api.Calls, c => c.Endpoint == "assets");
    }

    [Fact]
    public async Task Fields_added_or_removed_in_Uptick_are_flagged()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1)];
        var (migrator, clock) = Migrator(db, api);
        await migrator.RunAsync(UptickRunKind.Full, null, null);

        api.Endpoints["assets"] = [new UptickResource("Asset", "1", """{"name":"x","barcode":"123"}""", "{}", null, null)];
        clock.Now = Now.AddHours(1);
        var run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        var result = Report(run).Single();
        Assert.Equal(["barcode"], result.FieldsAdded);
        Assert.Equal(["location", "rel:property"], result.FieldsRemoved);
    }

    [Fact]
    public async Task Times_are_whole_milliseconds_so_they_read_back_from_Postgres_unchanged()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = [Asset(1)];
        var (migrator, clock) = Migrator(db, api);
        clock.Now = Now.AddTicks(1234567); // 123.4567 ms: finer than Postgres keeps

        var run = await migrator.RunAsync(UptickRunKind.Full, null, null);

        var row = await db.UptickRecords.SingleAsync();
        Assert.Equal(0, row.SeenUtc.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(0, run.StartedUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public async Task Overlapping_pages_count_each_record_once()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi { PageSize = 3 };
        // Uptick's pages overlap (UptickApiClient.PageOverlap): records 3 and 5 come twice
        api.Endpoints["assets"] = [Asset(1), Asset(2), Asset(3), Asset(3), Asset(4), Asset(5), Asset(5), Asset(6)];

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null);

        var result = Report(run).Single();
        Assert.Equal((6, 6, 0), (result.Received, result.Created, result.Unchanged));
    }

    [Fact]
    public async Task A_cancelled_run_keeps_what_it_copied_and_says_so()
    {
        await using var db = DbContextFactory.Create();
        var api = new FakeApi();
        api.Endpoints["assets"] = Enumerable.Range(1, 6).Select(i => Asset(i)).ToList();
        using var cancel = new CancellationTokenSource();
        var pages = 0;
        api.OnPage = () => { if (++pages == 2) cancel.Cancel(); };

        var run = await Migrator(db, api).Migrator.RunAsync(UptickRunKind.Full, null, null, ct: cancel.Token);

        Assert.Equal("Cancelled", run.Status);
        Assert.NotNull(run.FinishedUtc);
        Assert.Equal(2, await db.UptickRecords.CountAsync()); // the first page
        Assert.Equal("Cancelled", (await db.UptickMigrationRuns.SingleAsync()).Status);
    }
}
