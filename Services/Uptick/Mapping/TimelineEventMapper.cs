using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Timeline;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's timelineevents -> TimelineEvent: every event of every record (tasks, invoices, ... as well as
// properties and assets), so a record's Timeline is there once FyreApp shows it. Events on a property or an asset
// FyreApp holds are linked to it; those held later are linked by the next mapping pass.
public class TimelineEventMapper : IUptickMapper
{
    public const string Property = "property";
    public const string Asset = "asset";

    private readonly AppDbContext _db;

    // Uptick content type id -> model ("19" -> "property"), and FyreApp's ids by Uptick id
    private Dictionary<string, string> _models = new();
    private Dictionary<long, int> _sites = new();
    private Dictionary<long, int> _assets = new();

    public TimelineEventMapper(AppDbContext db) => _db = db;

    public string Resource => "timelineevents";

    public async Task BeginAsync(CancellationToken ct)
    {
        _models = new Dictionary<string, string>();
        foreach (var t in await RawAsync("contenttypes", ct))
            if (Str(t.Attributes, "model") is { } model) _models[t.UptickId] = model;

        _sites = (await _db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Id);

        _assets = (await _db.Assets.AsNoTracking().Where(a => a.UptickId != null)
                .Select(a => new { UptickId = a.UptickId!.Value, a.Id }).ToListAsync(ct))
            .ToDictionary(a => a.UptickId, a => a.Id);

        await LinkHeldSinceAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> MapAsync(IReadOnlyList<UptickRecord> batch, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>();
        var ids = batch.Select(r => long.TryParse(r.UptickId, out var id) ? id : (long?)null).Where(id => id != null).ToList();
        var existing = await _db.TimelineEvents
            .Where(e => ids.Contains(e.UptickId))
            .ToDictionaryAsync(e => e.UptickId!.Value, ct);

        foreach (var record in batch)
        {
            if (!long.TryParse(record.UptickId, out var uptickId))
            {
                failures[record.UptickId] = "The id isn't a number.";
                continue;
            }
            try
            {
                using var attributes = JsonDocument.Parse(record.Attributes);
                using var relationships = JsonDocument.Parse(record.Relationships);
                var a = attributes.RootElement;
                existing.TryGetValue(uptickId, out var e);

                // Deleted in Uptick (or flagged so): off the Timeline. The raw copy keeps it.
                if (record.DeletedUtc != null || a.TryGetProperty("is_deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
                {
                    if (e != null) _db.TimelineEvents.Remove(e);
                    continue;
                }

                if (e == null)
                {
                    e = new TimelineEvent { UptickId = uptickId };
                    _db.TimelineEvents.Add(e);
                }
                Map(e, record, a, relationships.RootElement);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                failures[record.UptickId] = ex.Message;
            }
        }

        await _db.SaveChangesAsync(ct);
        return failures;
    }

    private void Map(TimelineEvent e, UptickRecord record, JsonElement a, JsonElement relationships)
    {
        var contentType = relationships.TryGetProperty("content_type", out var ct) && ct.TryGetProperty("data", out var d)
            && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var ctId)
                ? ctId.ToString()
                : null;
        var objectId = Long(a, "object_id");

        e.UptickObjectType = contentType == null ? null : _models.GetValueOrDefault(contentType, contentType);
        e.UptickObjectId = objectId;
        e.PropertyId = e.UptickObjectType == Property && objectId is { } p && _sites.TryGetValue(p, out var siteId) ? siteId : null;
        e.AssetId = e.UptickObjectType == Asset && objectId is { } x && _assets.TryGetValue(x, out var assetId) ? assetId : null;

        e.WhenUtc = record.CreatedUtc ?? record.UpdatedUtc ?? record.FetchedUtc;
        e.Author = Str(a, "author_name")?.Trim();
        if (e.Author?.Length > 200) e.Author = e.Author[..200];
        e.Key = Str(a, "key");
        // Uptick's type C ("comment") is a note someone added; E is an event Uptick logged
        e.IsNote = Str(a, "type") == "C";
        e.Html = TimelineText.Render(Str(a, "text"), Get(a, "data"), Get(a, "data_links"));
        e.Latitude = Double(a, "coord_lat");
        e.Longitude = Double(a, "coord_lng");
        e.Field = null;
        e.Value = null;
        e.Text = null;
    }

    // Events whose property or asset wasn't in FyreApp when they were mapped, and is now
    private async Task LinkHeldSinceAsync(CancellationToken ct)
    {
        var properties = await _db.TimelineEvents
            .Where(e => e.UptickObjectType == Property && e.UptickObjectId != null && e.PropertyId == null)
            .Select(e => e.UptickObjectId!.Value).Distinct().ToListAsync(ct);
        foreach (var id in properties.Where(_sites.ContainsKey))
        {
            var siteId = _sites[id];
            await SetAsync(_db.TimelineEvents.Where(e => e.UptickObjectType == Property && e.UptickObjectId == id && e.PropertyId == null),
                e => e.PropertyId = siteId, s => s.SetProperty(e => e.PropertyId, siteId), ct);
        }

        var assets = await _db.TimelineEvents
            .Where(e => e.UptickObjectType == Asset && e.UptickObjectId != null && e.AssetId == null)
            .Select(e => e.UptickObjectId!.Value).Distinct().ToListAsync(ct);
        foreach (var id in assets.Where(_assets.ContainsKey))
        {
            var assetId = _assets[id];
            await SetAsync(_db.TimelineEvents.Where(e => e.UptickObjectType == Asset && e.UptickObjectId == id && e.AssetId == null),
                e => e.AssetId = assetId, s => s.SetProperty(e => e.AssetId, assetId), ct);
        }
    }

    private async Task SetAsync(IQueryable<TimelineEvent> events, Action<TimelineEvent> set,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<TimelineEvent>> setters, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            await events.ExecuteUpdateAsync(setters, ct);
            return;
        }

        // The in-memory provider the tests use has no bulk update
        foreach (var e in await events.ToListAsync(ct)) set(e);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task<List<(string UptickId, JsonElement Attributes)>> RawAsync(string resource, CancellationToken ct)
    {
        var rows = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Attributes })
            .ToListAsync(ct);
        return rows.Select(r => (r.UptickId, JsonDocument.Parse(r.Attributes).RootElement.Clone())).ToList();
    }

    private static JsonElement Get(JsonElement o, string name) => UptickRaw.Get(o, name);
    private static string? Str(JsonElement o, string name) => UptickRaw.Str(o, name);
    private static long? Long(JsonElement o, string name) => UptickRaw.Long(o, name);
    private static double? Double(JsonElement o, string name) => UptickRaw.Double(o, name);
}
