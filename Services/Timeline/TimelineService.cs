using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Timeline;

public interface ITimelineService
{
    Task<IReadOnlyList<TimelineEntry>> ForPropertyAsync(Site site, bool notesOnly, CancellationToken ct = default);
    Task<IReadOnlyList<TimelineEntry>> ForAssetAsync(Asset asset, bool notesOnly, CancellationToken ct = default);

    // Saves the entries (changes, events or a note)
    Task AddAsync(IEnumerable<TimelineEntry> entries, CancellationToken ct = default);
}

public class TimelineService : ITimelineService
{
    private const int Size = 200;

    private readonly AppDbContext _db;
    public TimelineService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TimelineEntry>> ForPropertyAsync(Site site, bool notesOnly, CancellationToken ct = default)
    {
        var entries = await _db.TimelineEntries.AsNoTracking()
            .Where(e => e.SiteId == site.Id && (!notesOnly || e.IsNote))
            .OrderByDescending(e => e.WhenUtc).ThenByDescending(e => e.Id).Take(Size)
            .ToListAsync(ct);

        // Imported properties were created in Uptick, which doesn't say by whom
        if (!notesOnly && site.ExternalId != null && site.Created is { } created)
            entries.Add(new TimelineEntry { SiteId = site.Id, WhenUtc = created, Author = TimelineLog.UptickAuthor, Text = "Property was created." });

        return entries;
    }

    public async Task<IReadOnlyList<TimelineEntry>> ForAssetAsync(Asset asset, bool notesOnly, CancellationToken ct = default)
    {
        var entries = await _db.TimelineEntries.AsNoTracking()
            .Where(e => e.AssetId == asset.Id && (!notesOnly || e.IsNote))
            .OrderByDescending(e => e.WhenUtc).ThenByDescending(e => e.Id).Take(Size)
            .ToListAsync(ct);

        if (!notesOnly && asset.ExternalId != null && TimelineLog.CreatedInUptick(asset.UptickData) is { } created)
            entries.Add(new TimelineEntry { AssetId = asset.Id, WhenUtc = created, Author = TimelineLog.UptickAuthor, Text = "Asset was created." });

        return entries;
    }

    public async Task AddAsync(IEnumerable<TimelineEntry> entries, CancellationToken ct = default)
    {
        _db.TimelineEntries.AddRange(entries);
        await _db.SaveChangesAsync(ct);
    }
}
