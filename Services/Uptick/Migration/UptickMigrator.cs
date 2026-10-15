using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Uptick.Api;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Migration;

public enum UptickRunKind
{
    // Every record of every endpoint; also finds records deleted in Uptick
    Full,
    // Only what changed since the last run, where Uptick can say what changed (§4.1)
    Refresh
}

// What a run did with one endpoint: the reconciliation report (§4.4)
public sealed class UptickEndpointResult
{
    public string Resource { get; set; } = string.Empty;

    // Ok, Empty, Forbidden, NotFound, Failed, Excluded, NotRun
    public string Status { get; set; } = "NotRun";

    // Full (every record) or Changes (updatedsince)
    public string Mode { get; set; } = "Full";

    // Uptick's own count for the query, and the distinct records it returned
    public int? UptickCount { get; set; }
    public int Received { get; set; }

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Restored { get; set; }
    public int Deleted { get; set; }

    // Records held for the endpoint afterwards (not deleted)
    public int Copied { get; set; }

    public bool? IncrementalVerified { get; set; }
    public string? IncrementalNote { get; set; }

    public List<string> FieldsAdded { get; set; } = [];
    public List<string> FieldsRemoved { get; set; } = [];

    public string? Warning { get; set; }
    public string? Error { get; set; }
    public int Requests { get; set; }
    public double Seconds { get; set; }
}

public sealed record UptickProgress(
    string? Resource, int EndpointNumber, int EndpointCount, int Received, int? Total, int Requests, int Throttled, string? Message = null);

// Copies Uptick's API into UptickRecord (docs/uptick-migration.md §4). Mapping into FyreApp's own tables comes in
// later phases and reads what this stores.
public class UptickMigrator
{
    // How far back the updatedsince check looks (§4.2)
    internal static readonly TimeSpan VerifyWindow = TimeSpan.FromDays(7);

    // A Refresh asks for changes since a little before the last one seen, in case of clock skew
    internal static readonly TimeSpan RefreshOverlap = TimeSpan.FromMinutes(10);

    // Don't mark records deleted when a full pull returned less than this share of what was held (an API hiccup)
    internal const double DeletionSafetyShare = 0.9;

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly IUptickApiClient _api;
    private readonly ILogger<UptickMigrator> _logger;
    private readonly TimeProvider _clock;

    public UptickMigrator(AppDbContext db, IUptickApiClient api, ILogger<UptickMigrator> logger, TimeProvider? clock = null)
    {
        _db = db;
        _api = api;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    // Whole milliseconds: Postgres keeps microseconds, so a time with finer ticks would read back earlier than it
    // was written, and a record seen this run would look unseen ("SeenUtc >= runStart" false)
    private DateTime Now
    {
        get
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        }
    }

    public async Task<UptickMigrationRun> RunAsync(UptickRunKind kind, string? onlyResource, string? startedBy,
        IProgress<UptickProgress>? progress = null, CancellationToken ct = default)
    {
        _api.ResetForRun();
        var run = new UptickMigrationRun
        {
            Kind = kind.ToString(), Resource = onlyResource, StartedBy = startedBy, StartedUtc = Now, Status = "Running"
        };
        _db.UptickMigrationRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var results = new List<UptickEndpointResult>();
        try
        {
            var states = await SyncEndpointListAsync(ct);
            var todo = states
                .Where(s => onlyResource == null || string.Equals(s.Resource, onlyResource, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Resource, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < todo.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var state = todo[i];
                if (!state.Enabled)
                {
                    results.Add(new UptickEndpointResult { Resource = state.Resource, Status = "Excluded", Warning = UptickEndpoints.WhyExcluded(state.Resource) });
                    continue;
                }

                var number = i + 1;
                results.Add(await MigrateEndpointAsync(state, kind,
                    p => progress?.Report(new UptickProgress(state.Resource, number, todo.Count, p.Received, p.Total, _api.Requests, _api.Throttled)),
                    ct));
            }

            run.Status = "Completed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Status = "Cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uptick migration run {RunId} failed", run.Id);
            run.Status = "Failed";
            run.Error = ex.Message;
        }

        // Saved even when cancelled, so the report shows how far it got
        _db.ChangeTracker.Clear();
        run.FinishedUtc = Now;
        run.Requests = _api.Requests;
        run.Throttled = _api.Throttled;
        run.Report = JsonSerializer.Serialize(results, ReportJson);
        _db.UptickMigrationRuns.Update(run);
        await _db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    // One state row per endpoint the API lists; excluded ones are kept but disabled
    private async Task<List<UptickEndpointState>> SyncEndpointListAsync(CancellationToken ct)
    {
        var listed = await _api.ListEndpointsAsync(ct);
        var states = await _db.UptickEndpointStates.ToDictionaryAsync(s => s.Resource, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var resource in listed)
        {
            if (!states.TryGetValue(resource, out var state))
            {
                state = new UptickEndpointState { Resource = resource };
                _db.UptickEndpointStates.Add(state);
                states[resource] = state;
            }
            state.Enabled = !UptickEndpoints.IsExcluded(resource);
            if (!state.Enabled) state.Status = "Excluded";
        }
        await _db.SaveChangesAsync(ct);
        return listed.Select(r => states[r]).ToList();
    }

    private async Task<UptickEndpointResult> MigrateEndpointAsync(UptickEndpointState state, UptickRunKind kind,
        Action<(int Received, int? Total)> report, CancellationToken ct)
    {
        var result = new UptickEndpointResult { Resource = state.Resource };
        var requestsBefore = _api.Requests;
        var started = Now;

        // Changes only when Uptick has proven it can say what changed; otherwise every record
        var changesOnly = kind == UptickRunKind.Refresh && state.IncrementalVerified && state.CursorUtc != null;
        var since = changesOnly ? state.CursorUtc - RefreshOverlap : null;
        result.Mode = changesOnly ? "Changes" : "Full";

        try
        {
            var pull = await PullAsync(state.Resource, since, started, result, report, ct);

            // Fewer records than Uptick counts: its pages aren't in a stable order (e.g. invoicelineitems, sorted
            // by a mostly-zero "index"), so offset paging skips some. Reported, not passed off as complete.
            var incomplete = !changesOnly && result.UptickCount is { } count && pull.Received < count;

            if (!changesOnly)
            {
                await MarkDeletionsAsync(state, started, pull.Received, result, ct);
                CheckSchema(state, pull.Fields, result);
                if (kind == UptickRunKind.Full && pull.MaxUpdatedUtc != null && !incomplete)
                    await VerifyIncrementalAsync(state, started, pull, result, ct);
            }

            if (pull.MaxUpdatedUtc is { } max && (state.CursorUtc == null || max > state.CursorUtc))
                state.CursorUtc = max;
            if (!changesOnly) state.UptickCount = result.UptickCount;

            result.Status = incomplete ? "Incomplete"
                : result.UptickCount == 0 && result.Received == 0 ? "Empty"
                : "Ok";
            state.LastError = null;
        }
        catch (UptickApiException ex) when (ex.StatusCode is 403 or 404)
        {
            // The API login can't see it, or this API version doesn't have it: reported, not a failure
            result.Status = ex.StatusCode == 403 ? "Forbidden" : "NotFound";
            result.Error = ex.Message;
            state.LastError = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UptickRateLimitedException)
        {
            _logger.LogWarning(ex, "Uptick endpoint {Resource} failed", state.Resource);
            result.Status = "Failed";
            result.Error = ex.Message;
            state.LastError = ex.Message;
        }

        // Pages clear the change tracker, so the state is saved as a whole
        _db.ChangeTracker.Clear();
        _db.UptickEndpointStates.Update(state);
        state.Status = result.Status;
        if (result.Status is "Ok" or "Empty")
        {
            if (kind == UptickRunKind.Full || !changesOnly) state.LastFullRunUtc = started;
            if (kind == UptickRunKind.Refresh) state.LastRefreshUtc = started;
        }
        state.CopiedCount = result.Copied = await _db.UptickRecords
            .CountAsync(r => r.Resource == state.Resource && r.DeletedUtc == null, CancellationToken.None);
        await _db.SaveChangesAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();

        result.Requests = _api.Requests - requestsBefore;
        result.Seconds = Math.Round((Now - started).TotalSeconds, 1);
        return result;
    }

    private sealed class Pull
    {
        public int Received;
        public DateTime? MaxUpdatedUtc;
        public List<string>? Fields;
        // Records with an "updated" inside the verify window, for the updatedsince check
        public Dictionary<string, DateTime> RecentlyUpdated { get; } = new();
    }

    // Fetches the endpoint page by page and stores what's new or changed
    private async Task<Pull> PullAsync(string resource, DateTime? since, DateTime runStart, UptickEndpointResult result,
        Action<(int, int?)> report, CancellationToken ct)
    {
        var pull = new Pull();
        var verifyFrom = runStart - VerifyWindow;

        await foreach (var page in _api.ListAsync(resource, since, ct))
        {
            result.UptickCount ??= page.Count;
            pull.Fields ??= page.Records.Count > 0 ? FieldsOf(page.Records[0]) : null;

            // Pages overlap a little (UptickApiClient.PageOverlap): the last copy of a record wins
            var records = page.Records.GroupBy(r => r.Id).Select(g => g.Last()).ToList();
            var ids = records.Select(r => r.Id).ToList();
            var existing = await _db.UptickRecords
                .Where(r => r.Resource == resource && ids.Contains(r.UptickId))
                .ToDictionaryAsync(r => r.UptickId, ct);

            var now = Now;
            foreach (var record in records)
            {
                if (record.UpdatedUtc is { } updated)
                {
                    if (pull.MaxUpdatedUtc == null || updated > pull.MaxUpdatedUtc) pull.MaxUpdatedUtc = updated;
                    if (updated >= verifyFrom) pull.RecentlyUpdated[record.Id] = updated;
                }

                if (!existing.TryGetValue(record.Id, out var row))
                {
                    _db.UptickRecords.Add(new UptickRecord
                    {
                        Resource = resource, UptickId = record.Id, Type = record.Type,
                        Attributes = record.Attributes, Relationships = record.Relationships, Hash = record.Hash,
                        CreatedUtc = record.CreatedUtc, UpdatedUtc = record.UpdatedUtc,
                        FetchedUtc = now, ChangedUtc = now, SeenUtc = runStart
                    });
                    result.Created++;
                    pull.Received++;
                    continue;
                }

                // Seen already this run (an overlapping page): nothing new to count
                var seenThisRun = row.SeenUtc >= runStart;
                if (!seenThisRun) pull.Received++;
                row.SeenUtc = runStart;
                if (row.Hash != record.Hash)
                {
                    row.Type = record.Type;
                    row.Attributes = record.Attributes;
                    row.Relationships = record.Relationships;
                    row.Hash = record.Hash;
                    row.CreatedUtc = record.CreatedUtc;
                    row.UpdatedUtc = record.UpdatedUtc;
                    row.ChangedUtc = now;
                    row.MappedUtc = null;
                    if (!seenThisRun) result.Updated++;
                }
                if (row.DeletedUtc != null)
                {
                    row.DeletedUtc = null;
                    row.MappedUtc = null;
                    result.Restored++;
                }
            }

            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();

            report((pull.Received, result.UptickCount));
        }

        result.Received = pull.Received;
        result.Unchanged = Math.Max(0, pull.Received - result.Created - result.Updated - result.Restored);
        return pull;
    }

    // A full pull that didn't return a record means Uptick deleted it (§4.3)
    private async Task MarkDeletionsAsync(UptickEndpointState state, DateTime runStart, int received, UptickEndpointResult result,
        CancellationToken ct)
    {
        if (result.UptickCount is { } count && received < count)
        {
            result.Warning = $"Uptick reported {count} records but returned {received}; deletions not checked this run.";
            return;
        }
        if (state.CopiedCount > 0 && received < state.CopiedCount * DeletionSafetyShare)
        {
            result.Warning = $"Only {received} of the {state.CopiedCount} records held came back; deletions not checked this run.";
            return;
        }

        while (true)
        {
            var gone = await _db.UptickRecords
                .Where(r => r.Resource == state.Resource && r.DeletedUtc == null && r.SeenUtc < runStart)
                .OrderBy(r => r.UptickId)
                .Take(5000)
                .ToListAsync(ct);
            if (gone.Count == 0) break;

            var now = Now;
            foreach (var row in gone)
            {
                row.DeletedUtc = now;
                row.MappedUtc = null;
            }
            result.Deleted += gone.Count;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }

    // Field names of a record, e.g. "name", "rel:property", to notice Uptick adding or removing fields
    private static List<string> FieldsOf(UptickResource record)
    {
        static IEnumerable<string> Names(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : [];
        }
        return Names(record.Attributes).Concat(Names(record.Relationships).Select(n => "rel:" + n))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static void CheckSchema(UptickEndpointState state, List<string>? fields, UptickEndpointResult result)
    {
        if (fields == null) return;
        if (state.Schema != null)
        {
            var before = JsonSerializer.Deserialize<List<string>>(state.Schema) ?? [];
            result.FieldsAdded = fields.Except(before).ToList();
            result.FieldsRemoved = before.Except(fields).ToList();
        }
        state.Schema = JsonSerializer.Serialize(fields);
    }

    // Checks updatedsince returns exactly the records changed since a time, so a Refresh can trust it (§4.2)
    private async Task VerifyIncrementalAsync(UptickEndpointState state, DateTime runStart, Pull pull, UptickEndpointResult result,
        CancellationToken ct)
    {
        var expected = pull.RecentlyUpdated;
        if (expected.Count == 0 || expected.Count >= pull.Received)
        {
            // Nothing changed lately, or everything did: the check can't tell a working filter from an ignored one
            result.IncrementalNote = "Not checked: needs some, but not all, records changed in the last 7 days.";
            result.IncrementalVerified = state.IncrementalVerified;
            return;
        }

        var got = new Dictionary<string, DateTime?>();
        await foreach (var page in _api.ListAsync(state.Resource, runStart - VerifyWindow, ct))
            foreach (var r in page.Records)
                got[r.Id] = r.UpdatedUtc;

        var missing = expected.Keys.Count(id => !got.ContainsKey(id));
        // Extra records are fine only if they changed during this run
        var extra = got.Count(g => !expected.ContainsKey(g.Key) && !(g.Value >= runStart));

        state.IncrementalVerified = missing == 0 && extra == 0;
        state.IncrementalCheckedUtc = Now;
        result.IncrementalVerified = state.IncrementalVerified;
        result.IncrementalNote = state.IncrementalVerified
            ? $"updatedsince returned the {expected.Count} records changed in the last 7 days."
            : $"updatedsince doesn't match: {missing} changed records missing, {extra} unchanged ones returned. Refresh pulls this endpoint in full.";
    }
}
