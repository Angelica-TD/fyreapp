using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Timeline;

public interface ITimelineService
{
    Task<IReadOnlyList<TimelineEvent>> ForPropertyAsync(Site site, bool notesOnly, CancellationToken ct = default);
    Task<IReadOnlyList<TimelineEvent>> ForAssetAsync(Asset asset, bool notesOnly, CancellationToken ct = default);

    // Saves the entries (changes, events or a note)
    Task AddAsync(IEnumerable<TimelineEvent> entries, CancellationToken ct = default);
}

public class TimelineService : ITimelineService
{
    private const int Size = 200;

    private readonly AppDbContext _db;
    public TimelineService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TimelineEvent>> ForPropertyAsync(Site site, bool notesOnly, CancellationToken ct = default)
    {
        return await _db.TimelineEvents.AsNoTracking()
            .Where(e => e.SiteId == site.Id && (!notesOnly || e.IsNote))
            .OrderByDescending(e => e.WhenUtc).ThenByDescending(e => e.Id).Take(Size)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TimelineEvent>> ForAssetAsync(Asset asset, bool notesOnly, CancellationToken ct = default)
    {
        return await _db.TimelineEvents.AsNoTracking()
            .Where(e => e.AssetId == asset.Id && (!notesOnly || e.IsNote))
            .OrderByDescending(e => e.WhenUtc).ThenByDescending(e => e.Id).Take(Size)
            .ToListAsync(ct);
    }

    public async Task AddAsync(IEnumerable<TimelineEvent> entries, CancellationToken ct = default)
    {
        _db.TimelineEvents.AddRange(entries);
        await _db.SaveChangesAsync(ct);
    }
}
