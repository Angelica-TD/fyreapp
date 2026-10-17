using FyreApp.Models;

namespace FyreApp.Services.Uptick.Mapping;

// Takes one endpoint's raw records (UptickRecord) into FyreApp's own table for them (docs/uptick-migration.md §3.4).
// Registered in the order they run: a mapper after the ones whose records it links to.
public interface IUptickMapper
{
    // The endpoint it reads, e.g. "timelineevents"
    string Resource { get; }

    // Once per mapping pass, before the first batch: load what records are linked through
    Task BeginAsync(CancellationToken ct);

    // Upserts the batch's records, keyed on their Uptick id, and removes or deactivates those deleted in Uptick.
    // Returns the records that couldn't be mapped (Uptick id -> why); they stay waiting.
    Task<IReadOnlyDictionary<string, string>> MapAsync(IReadOnlyList<UptickRecord> batch, CancellationToken ct);
}
