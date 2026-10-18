using System.Diagnostics;
using FyreApp.Data;
using FyreApp.Services.Uptick.Migration;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Runs the mappers over the raw copy (docs/uptick-migration.md §3.4): the records waiting to be mapped (new or
// changed since, MappedUtc null) after every Full or Refresh run, or every record of an endpoint on a Re-map.
public class UptickMapping
{
    internal const int BatchSize = 5000;

    // Errors kept in the run report per endpoint
    private const int ErrorsReported = 5;

    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IUptickMapper> _mappers;
    private readonly ILogger<UptickMapping> _logger;
    private readonly TimeProvider _clock;

    public UptickMapping(AppDbContext db, IEnumerable<IUptickMapper> mappers, ILogger<UptickMapping> logger, TimeProvider? clock = null)
    {
        _db = db;
        _mappers = mappers.ToList();
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    // The endpoints mapped into FyreApp's tables, in the order they're mapped
    public IReadOnlyList<string> Resources => _mappers.Select(m => m.Resource).ToList();

    // Maps the endpoint's waiting records (all of them when remapping) and adds what happened to the result
    public async Task MapAsync(string resource, bool remap, UptickEndpointResult result, Action<int>? progress, CancellationToken ct)
    {
        var mapper = _mappers.First(m => string.Equals(m.Resource, resource, StringComparison.OrdinalIgnoreCase));
        var watch = Stopwatch.StartNew();
        result.Mapped ??= 0;
        try
        {
            if (remap) await ClearMappedAsync(mapper.Resource, ct);
            await mapper.BeginAsync(ct);
            _db.ChangeTracker.Clear();

            var failed = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var skip = failed.ToList();
                // No ORDER BY: the partial index on waiting records hands them over as they are, and each batch
                // leaves it once mapped
                var batch = await _db.UptickRecords.AsNoTracking()
                    .Where(r => r.Resource == mapper.Resource && r.MappedUtc == null && !skip.Contains(r.UptickId))
                    .Take(BatchSize)
                    .ToListAsync(ct);
                if (batch.Count == 0) break;

                var failures = await mapper.MapAsync(batch, ct);
                _db.ChangeTracker.Clear();
                foreach (var (id, why) in failures)
                {
                    failed.Add(id);
                    if (result.MapErrors.Count < ErrorsReported) result.MapErrors.Add($"{id}: {why}");
                }

                var mapped = batch.Select(r => r.UptickId).Where(id => !failures.ContainsKey(id)).ToList();
                await MarkMappedAsync(mapper.Resource, mapped, ct);
                result.Mapped += mapped.Count;
                progress?.Invoke(result.Mapped ?? 0);
            }
            result.MapFailed = failed.Count;

            var state = await _db.UptickEndpointStates.FirstOrDefaultAsync(s => s.Resource == mapper.Resource, ct);
            if (state != null)
            {
                state.MappedCount = await _db.UptickRecords
                    .CountAsync(r => r.Resource == mapper.Resource && r.MappedUtc != null && r.DeletedUtc == null, ct);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Mapping Uptick {Resource} failed", mapper.Resource);
            result.MapError = ex.Message;
        }
        finally
        {
            _db.ChangeTracker.Clear();
            result.MapSeconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
        }
    }

    private async Task ClearMappedAsync(string resource, CancellationToken ct)
    {
        var rows = _db.UptickRecords.Where(r => r.Resource == resource && r.MappedUtc != null);
        if (_db.Database.IsRelational())
        {
            // One statement over every record of the endpoint: ~1 M timeline events take longer than the default 30 s
            var timeout = _db.Database.GetCommandTimeout();
            _db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
            try
            {
                await rows.ExecuteUpdateAsync(s => s.SetProperty(r => r.MappedUtc, (DateTime?)null), ct);
            }
            finally
            {
                _db.Database.SetCommandTimeout(timeout);
            }
            return;
        }

        // The in-memory provider the tests use has no bulk update
        foreach (var row in await rows.ToListAsync(ct)) row.MappedUtc = null;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task MarkMappedAsync(string resource, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        var rows = _db.UptickRecords.Where(r => r.Resource == resource && ids.Contains(r.UptickId));
        if (_db.Database.IsRelational())
        {
            await rows.ExecuteUpdateAsync(s => s.SetProperty(r => r.MappedUtc, now), ct);
            return;
        }

        foreach (var row in await rows.ToListAsync(ct)) row.MappedUtc = now;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }
}
