using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Uptick.Api;
using FyreApp.Services.Uptick.Mapping;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Migration;

public enum UptickRunKind
{
    // Every record of every endpoint; also finds records deleted in Uptick
    Full,
    // Only what changed since the last run, where Uptick can say what changed (§4.1)
    Refresh,
    // No requests to Uptick: maps every record held for the mapped endpoints again (§3.4), e.g. after a mapper changes
    Remap,
    // Downloads the files the raw copy lists (§8) that aren't held yet or changed: report PDFs, document files
    Files
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

    // Endpoints filled one parent at a time: how many parents were fetched (§4.5)
    public string? Note { get; set; }

    public List<string> FieldsAdded { get; set; } = [];
    public List<string> FieldsRemoved { get; set; } = [];

    public string? Warning { get; set; }
    public string? Error { get; set; }
    public int Requests { get; set; }
    public double Seconds { get; set; }

    // Endpoints mapped into FyreApp's own tables (§3.4): records taken in this run, records that couldn't be (with
    // the first few reasons), or why mapping stopped
    public int? Mapped { get; set; }
    public int MapFailed { get; set; }
    public List<string> MapErrors { get; set; } = [];
    public string? MapError { get; set; }
    public double? MapSeconds { get; set; }
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

    // Gap between the updatedsince dates a per-parent endpoint still short after its fill is paged under (§4.5)
    internal const int SweepStepMonths = 2;

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly IUptickApiClient _api;
    private readonly ILogger<UptickMigrator> _logger;
    private readonly TimeProvider _clock;
    private readonly UptickMapping? _mapping;
    private readonly Files.UptickFileDownloader? _files;

    public UptickMigrator(AppDbContext db, IUptickApiClient api, ILogger<UptickMigrator> logger, TimeProvider? clock = null,
        UptickMapping? mapping = null, Files.UptickFileDownloader? files = null)
    {
        _files = files;
        _db = db;
        _api = api;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _mapping = mapping;
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
        IProgress<UptickProgress>? progress = null, CancellationToken ct = default, int? limit = null)
    {
        _api.ResetForRun();
        var run = new UptickMigrationRun
        {
            Kind = kind.ToString(), Resource = limit is { } n ? $"first {n} of each" : onlyResource, StartedBy = startedBy,
            StartedUtc = Now, Status = "Running"
        };
        _db.UptickMigrationRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var results = new List<UptickEndpointResult>();
        try
        {
            if (kind == UptickRunKind.Remap)
                await RemapAsync(onlyResource, results, progress, ct);
            else if (kind == UptickRunKind.Files)
                await (_files ?? throw new InvalidOperationException("File downloads aren't set up.")).DownloadAsync(results, progress, ct, limit);
            else
                await CopyAsync(kind, onlyResource, results, progress, ct);
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

    // Copies the endpoints, then maps what came in new or changed into FyreApp's own tables
    private async Task CopyAsync(UptickRunKind kind, string? onlyResource, List<UptickEndpointResult> results,
        IProgress<UptickProgress>? progress, CancellationToken ct)
    {
        var states = await SyncEndpointListAsync(ct);
        var todo = states
            .Where(s => onlyResource == null || string.Equals(s.Resource, onlyResource, StringComparison.OrdinalIgnoreCase))
            // Endpoints filled per parent go after the parent they're checked against
            .OrderBy(s => UptickEndpoints.FilledPerParent.ContainsKey(s.Resource))
            .ThenBy(s => s.Resource, StringComparer.Ordinal)
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
            var result = await MigrateEndpointAsync(state, kind,
                p => progress?.Report(new UptickProgress(state.Resource, number, todo.Count, p.Received, p.Total, _api.Requests, _api.Throttled)),
                ct);
            results.Add(result);
            if (UptickEndpoints.WithLinks.TryGetValue(state.Resource, out var links) && result.Status is "Ok" or "Empty")
                results.Add(await MigrateLinksAsync(state.Resource, links,
                    p => progress?.Report(new UptickProgress(state.Resource + UptickEndpoints.LinksSuffix, number, todo.Count, p.Received, p.Total, _api.Requests, _api.Throttled)),
                    ct));
        }

        await MapWaitingAsync(results, progress, ct);
    }

    // Maps the records waiting in each mapped endpoint the run copied, adding to its result
    private async Task MapWaitingAsync(List<UptickEndpointResult> results, IProgress<UptickProgress>? progress, CancellationToken ct)
    {
        if (_mapping == null) return;
        foreach (var resource in _mapping.Resources)
        {
            var result = results.FirstOrDefault(r => string.Equals(r.Resource, resource, StringComparison.OrdinalIgnoreCase));
            if (result == null || result.Status is "Excluded" or "NotRun") continue;
            await MapOneAsync(resource, remap: false, result, progress, ct);
        }
    }

    // Maps every record held again, without asking Uptick for anything
    private async Task RemapAsync(string? onlyResource, List<UptickEndpointResult> results, IProgress<UptickProgress>? progress,
        CancellationToken ct)
    {
        if (_mapping == null) return;
        foreach (var resource in _mapping.Resources.Where(r => onlyResource == null || string.Equals(r, onlyResource, StringComparison.OrdinalIgnoreCase)))
        {
            var result = new UptickEndpointResult { Resource = resource, Mode = "Map" };
            results.Add(result);
            await MapOneAsync(resource, remap: true, result, progress, ct);
            result.Status = result.MapError == null ? "Ok" : "Failed";
            result.Copied = await _db.UptickRecords.CountAsync(r => r.Resource == resource && r.DeletedUtc == null, ct);
        }
    }

    private async Task MapOneAsync(string resource, bool remap, UptickEndpointResult result, IProgress<UptickProgress>? progress,
        CancellationToken ct)
    {
        // Run even with nothing waiting: records FyreApp has taken in since (e.g. from a file import) get linked
        var total = await _db.UptickRecords.CountAsync(r => r.Resource == resource && (remap || r.MappedUtc == null), ct);
        progress?.Report(new UptickProgress(resource, 1, 1, 0, total, _api.Requests, _api.Throttled, $"Mapping {resource}"));
        await _mapping!.MapAsync(resource, remap, result,
            mapped => progress?.Report(new UptickProgress(resource, 1, 1, mapped, total, _api.Requests, _api.Throttled, $"Mapping {resource}")),
            ct);
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

            // Pages that skip records (invoicelineitems): the rest are fetched one parent at a time, and what's
            // held is what counts against Uptick's count, as records filled in earlier runs needn't come again
            var perParent = changesOnly ? null : UptickEndpoints.FilledPerParent.GetValueOrDefault(state.Resource);
            var got = pull.Received;
            if (perParent != null)
            {
                await FillPerParentAsync(state.Resource, perParent, started, result, pull, report, ct);
                got = await _db.UptickRecords.CountAsync(r => r.Resource == state.Resource && r.DeletedUtc == null, ct);
                if (result.UptickCount is { } uptickCount && got < uptickCount)
                    got = await SweepAsync(state.Resource, uptickCount, got, started, result, pull, report, ct);
            }
            result.Received = pull.Received;
            result.Unchanged = Math.Max(0, pull.Received - result.Created - result.Updated - result.Restored);

            // Fewer records than Uptick counts: its pages aren't in a stable order, so offset paging skipped some.
            // Reported, not passed off as complete.
            var incomplete = !changesOnly && result.UptickCount is { } count && got < count;
            if (incomplete && perParent != null)
                result.Warning = $"Uptick reported {result.UptickCount} records; {got} are held.";

            if (!changesOnly)
            {
                // Per-parent endpoints find their deletions parent by parent instead
                if (perParent == null) await MarkDeletionsAsync(state, started, pull.Received, result, ct);
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

    // An endpoint's to-many relationships (UptickEndpoints.WithLinks), all of them every run: a change to them
    // needn't move the record's "updated". Kept as "<endpoint>.links"; one no longer returned is marked deleted.
    private async Task<UptickEndpointResult> MigrateLinksAsync(string resource, UptickEndpoints.Links links,
        Action<(int Received, int? Total)> report, CancellationToken ct)
    {
        var linksResource = resource + UptickEndpoints.LinksSuffix;
        var result = new UptickEndpointResult { Resource = linksResource };
        var requestsBefore = _api.Requests;
        var started = Now;
        try
        {
            var pull = new Pull();
            await foreach (var page in _api.ListAsync(resource, fields: (links.Type, string.Join(',', links.Names)), ct: ct))
            {
                result.UptickCount ??= page.Count;
                await StorePageAsync(linksResource, page.Records, started, result, pull, ct);
                report((pull.Received, result.UptickCount));
            }
            result.Received = pull.Received;
            result.Unchanged = Math.Max(0, pull.Received - result.Created - result.Updated - result.Restored);

            var incomplete = result.UptickCount is { } count && pull.Received < count;
            if (!incomplete)
            {
                var gone = await _db.UptickRecords.AsNoTracking()
                    .Where(r => r.Resource == linksResource && r.DeletedUtc == null && r.SeenUtc < started)
                    .Select(r => r.UptickId)
                    .ToListAsync(ct);
                await MarkDeletedAsync(linksResource, gone, result, ct);
            }
            result.Status = incomplete ? "Incomplete" : result.Received == 0 ? "Empty" : "Ok";
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UptickRateLimitedException)
        {
            _logger.LogWarning(ex, "Uptick links {Resource} failed", linksResource);
            result.Status = "Failed";
            result.Error = ex.Message;
        }

        _db.ChangeTracker.Clear();
        result.Copied = await _db.UptickRecords.CountAsync(r => r.Resource == linksResource && r.DeletedUtc == null, CancellationToken.None);
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

        await foreach (var page in _api.ListAsync(resource, since, ct: ct))
        {
            result.UptickCount ??= page.Count;
            pull.Fields ??= page.Records.Count > 0 ? FieldsOf(page.Records[0]) : null;

            await StorePageAsync(resource, page.Records, runStart, result, pull, ct);
            report((pull.Received, result.UptickCount));
        }

        return pull;
    }

    // Stores what's new or changed in one page, counting each record once per run
    private async Task StorePageAsync(string resource, IReadOnlyList<UptickResource> pageRecords, DateTime runStart,
        UptickEndpointResult result, Pull pull, CancellationToken ct)
    {
        var verifyFrom = runStart - VerifyWindow;

        // Pages overlap a little (UptickApiClient.PageOverlap): the last copy of a record wins
        var records = pageRecords.GroupBy(r => r.Id).Select(g => g.Last()).ToList();
        var ids = records.Select(r => r.Id).ToList();
        // Only what's needed to tell new, changed and unchanged apart: not the JSON, which on a repeat run would
        // be read back for every record
        var existing = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && ids.Contains(r.UptickId))
            .Select(r => new { r.UptickId, r.Hash, r.SeenUtc, r.DeletedUtc })
            .ToDictionaryAsync(r => r.UptickId, ct);

        // Records that changed or came back are loaded whole and updated; the rest only need marking seen
        var changedIds = records
            .Where(r => existing.TryGetValue(r.Id, out var e) && (e.Hash != r.Hash || e.DeletedUtc != null))
            .Select(r => r.Id)
            .ToList();
        var changed = changedIds.Count == 0
            ? new Dictionary<string, UptickRecord>()
            : await _db.UptickRecords
                .Where(r => r.Resource == resource && changedIds.Contains(r.UptickId))
                .ToDictionaryAsync(r => r.UptickId, ct);
        var unchangedUnseen = new List<string>();

        var now = Now;
        foreach (var record in records)
        {
            if (record.UpdatedUtc is { } updated)
            {
                if (pull.MaxUpdatedUtc == null || updated > pull.MaxUpdatedUtc) pull.MaxUpdatedUtc = updated;
                if (updated >= verifyFrom) pull.RecentlyUpdated[record.Id] = updated;
            }

            if (!existing.TryGetValue(record.Id, out var held))
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
            var seenThisRun = held.SeenUtc >= runStart;
            if (!seenThisRun) pull.Received++;
            if (!changed.TryGetValue(record.Id, out var row))
            {
                if (!seenThisRun) unchangedUnseen.Add(record.Id);
                continue;
            }

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

        // One statement for the page, not an update per record: on a repeat run almost every record is unchanged
        if (unchangedUnseen.Count == 0) return;
        var unseen = _db.UptickRecords.Where(r => r.Resource == resource && unchangedUnseen.Contains(r.UptickId));
        if (_db.Database.IsRelational())
        {
            await unseen.ExecuteUpdateAsync(s => s.SetProperty(r => r.SeenUtc, runStart), ct);
            return;
        }

        // The in-memory provider the tests use has no bulk update
        foreach (var row in await unseen.ToListAsync(ct)) row.SeenUtc = runStart;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    // Fetches again, one parent at a time, the records of every parent whose records held don't add up to it, or
    // that has none (§4.5): what paging skipped, and what changed or was deleted since. A parent's fetch returns
    // all its records, so any held for it that don't come back were deleted in Uptick.
    private async Task FillPerParentAsync(string resource, UptickEndpoints.PerParent spec, DateTime runStart,
        UptickEndpointResult result, Pull pull, Action<(int, int?)> report, CancellationToken ct)
    {
        var parents = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == spec.Parent)
            .Select(r => new { r.UptickId, r.Attributes, r.DeletedUtc, r.ChangedUtc, r.ChildrenFetchedUtc })
            .ToListAsync(ct);
        var children = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Attributes, r.Relationships })
            .ToListAsync(ct);

        var held = children
            .Select(c => (c.UptickId, Parent: RelatedId(c.Relationships, spec.Filter), Amount: Amount(c.Attributes, spec.ChildAmount)))
            .Where(c => c.Parent != null)
            .GroupBy(c => c.Parent!)
            .ToDictionary(g => g.Key, g => (Ids: g.Select(c => c.UptickId).ToHashSet(), Sum: g.Sum(c => c.Amount)));

        // Records of parents deleted in Uptick went with them
        var deletedParents = parents.Where(p => p.DeletedUtc != null).Select(p => p.UptickId).ToHashSet();
        await MarkDeletedAsync(resource, held.Where(h => deletedParents.Contains(h.Key)).SelectMany(h => h.Value.Ids).ToList(), result, ct);

        // Fetched before and unchanged since: what it holds is all Uptick has, even if it doesn't add up (no lines
        // in Uptick, as with invoices synced from Xero, or a discount). A line added or removed changes the invoice.
        var todo = parents
            .Where(p => p.DeletedUtc == null && (p.ChildrenFetchedUtc == null || p.ChangedUtc > p.ChildrenFetchedUtc))
            .Where(p => !held.TryGetValue(p.UptickId, out var h) || Math.Abs(h.Sum - Amount(p.Attributes, spec.ParentAmount)) >= 0.005m)
            .Select(p => p.UptickId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        result.Note = $"{todo.Count:N0} {spec.Parent} fetched one at a time (records not adding up to their {spec.ParentAmount}, and not fetched since they last changed).";

        foreach (var parentId in todo)
        {
            ct.ThrowIfCancellationRequested();
            var returned = new HashSet<string>();
            await foreach (var page in _api.ListAsync(resource, filter: (spec.Filter, parentId), ct: ct))
            {
                // A filter Uptick ignored would return other parents' records, and make this parent's look deleted
                if (page.Records.FirstOrDefault(r => RelatedId(r.Relationships, spec.Filter) != parentId) is { } other)
                    throw new UptickApiException(0,
                        $"Uptick ignored the {spec.Filter} filter on {resource} (asked for {parentId}, got a record of {RelatedId(other.Relationships, spec.Filter) ?? "none"}).");
                await StorePageAsync(resource, page.Records, runStart, result, pull, ct);
                returned.UnionWith(page.Records.Select(r => r.Id));
            }

            if (held.TryGetValue(parentId, out var h))
                await MarkDeletedAsync(resource, h.Ids.Where(id => !returned.Contains(id)).ToList(), result, ct);
            var parent = await _db.UptickRecords.SingleAsync(r => r.Resource == spec.Parent && r.UptickId == parentId, ct);
            parent.ChildrenFetchedUtc = Now;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            report((pull.Received, result.UptickCount));
        }
    }

    // Records the fill can't reach: a $0 line, or one its invoice's subtotal leaves out, doesn't change the sum, so
    // the invoice looks complete and isn't fetched. updatedsince is the only filter Uptick honours on them, and each
    // date returns the records in another order, so paging under a series of dates turns up the ones plain paging
    // skips. Goes back until nothing is missing or past the oldest record held. Returns the records held.
    private async Task<int> SweepAsync(string resource, int uptickCount, int held, DateTime runStart,
        UptickEndpointResult result, Pull pull, Action<(int, int?)> report, CancellationToken ct)
    {
        var oldest = await _db.UptickRecords
            .Where(r => r.Resource == resource && r.DeletedUtc == null)
            .MinAsync(r => r.UpdatedUtc, ct);
        if (oldest == null) return held;

        var before = held;
        var dates = 0;
        for (var since = runStart.AddMonths(-SweepStepMonths); held < uptickCount; since = since.AddMonths(-SweepStepMonths))
        {
            await foreach (var page in _api.ListAsync(resource, since, ct: ct))
            {
                await StorePageAsync(resource, page.Records, runStart, result, pull, ct);
                report((pull.Received, result.UptickCount));
            }
            dates++;
            held = await _db.UptickRecords.CountAsync(r => r.Resource == resource && r.DeletedUtc == null, ct);
            if (since <= oldest) break;
        }

        result.Note += $" {held - before:N0} more found paging under {dates} updatedsince dates.";
        return held;
    }

    private async Task MarkDeletedAsync(string resource, List<string> ids, UptickEndpointResult result, CancellationToken ct)
    {
        foreach (var chunk in ids.Chunk(5000))
        {
            var rows = await _db.UptickRecords
                .Where(r => r.Resource == resource && chunk.Contains(r.UptickId) && r.DeletedUtc == null)
                .ToListAsync(ct);
            var now = Now;
            foreach (var row in rows)
            {
                row.DeletedUtc = now;
                row.MappedUtc = null;
            }
            result.Deleted += rows.Count;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }

    // The id of a JSON:API to-one relationship, e.g. "invoice": {"data": {"id": "129"}}
    private static string? RelatedId(string relationships, string name)
    {
        using var doc = JsonDocument.Parse(relationships);
        return doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty(name, out var rel) && rel.ValueKind == JsonValueKind.Object
               && rel.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
               && data.TryGetProperty("id", out var id)
            ? id.ToString()
            : null;
    }

    // A money attribute, which Uptick sends as a string ("67.5000"); missing or blank counts as 0
    private static decimal Amount(string attributes, string name)
    {
        using var doc = JsonDocument.Parse(attributes);
        if (!doc.RootElement.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
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
        await foreach (var page in _api.ListAsync(state.Resource, runStart - VerifyWindow, ct: ct))
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
