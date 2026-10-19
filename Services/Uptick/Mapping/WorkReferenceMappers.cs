using System.Text.Json;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Imports.Uptick;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's routineservicetypes -> RoutineServiceType ("10 - Portable and Wheeled Fire Extinguishers"). One the
// routines or levels exports made by name is claimed by its name.
public class RoutineServiceTypeMapper : UptickEntityMapper<RoutineServiceType>
{
    private Dictionary<long, string> _standards = new();
    private Dictionary<long, string> _serviceGroups = new();

    public RoutineServiceTypeMapper(AppDbContext db) : base(db) { }

    public override string Resource => "routineservicetypes";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _standards = await RawNamesAsync(Db, "standards", ct);
        _serviceGroups = await RawNamesAsync(Db, "servicegroups", ct);
    }

    protected override async Task<Dictionary<long, RoutineServiceType>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.RoutineServiceTypes
            .Where(t => (t.UptickId != null && ids.Contains(t.UptickId.Value)) || (t.ExternalId != null && texts.Contains(t.ExternalId))
                || (t.UptickId == null && t.ExternalId == null))
            .ToListAsync(ct);
        var map = Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), t => t.UptickId, t => t.ExternalId);

        var unlinked = rows.Where(t => t.UptickId == null && t.ExternalId == null)
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var raw in batch.Where(r => !map.ContainsKey(r.Id)))
            if (raw.Text("name") is { } name && unlinked.Remove(name, out var type))
                map[raw.Id] = type;
        return map;
    }

    protected override RoutineServiceType Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(RoutineServiceType t, UptickRaw raw)
    {
        t.UptickId = raw.Id;
        t.ExternalId = raw.Id.ToString();
        t.Name = raw.Text("name", 200) ?? $"Routine {raw.Id}";
        t.Standard = Cap(raw.Rel("standard") is { } s ? _standards.GetValueOrDefault(s) : null, 100);
        t.StandardReference = raw.Text("standard_reference", 100);
        t.StandardNotes = raw.Text("standard_notes");
        t.DefaultPerformanceStandard = raw.Text("default_performance_standard", 200);
        t.ServiceGroup = Cap(raw.Rel("servicegroup") is { } g ? _serviceGroups.GetValueOrDefault(g) : null, 100);
        t.Custom = raw.Bool("is_custom") ?? false;
        t.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(RoutineServiceType t) => t.Active = false;

    // Id -> name of a small reference endpoint held only in the raw copy (standards, service groups)
    internal static async Task<Dictionary<long, string>> RawNamesAsync(AppDbContext db, string resource, CancellationToken ct)
    {
        var names = new Dictionary<long, string>();
        var rows = await db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Attributes })
            .ToListAsync(ct);
        foreach (var r in rows)
        {
            using var a = JsonDocument.Parse(r.Attributes);
            if (long.TryParse(r.UptickId, out var id) && UptickRaw.Str(a.RootElement, "name")?.Trim() is { Length: > 0 } name)
                names[id] = name;
        }
        return names;
    }

    internal static string? Cap(string? s, int max) => s?.Length > max ? s[..max] : s;
}

// Uptick's routineserviceleveltypes -> RoutineServiceLevel ("Six-monthly" of a routine service type). (Uptick's
// routineservicelevels are the levels set up at each property, which go with routine services.)
public class RoutineServiceLevelMapper : UptickEntityMapper<RoutineServiceLevel>
{
    private Dictionary<long, int> _types = new();

    public RoutineServiceLevelMapper(AppDbContext db) : base(db) { }

    public override string Resource => "routineserviceleveltypes";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _types = (await Db.RoutineServiceTypes.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
    }

    protected override async Task<Dictionary<long, RoutineServiceLevel>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.RoutineServiceLevels
            .Where(l => (l.UptickId != null && ids.Contains(l.UptickId.Value)) || (l.ExternalId != null && texts.Contains(l.ExternalId))
                || (l.UptickId == null && l.ExternalId == null))
            .ToListAsync(ct);
        var map = Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), l => l.UptickId, l => l.ExternalId);

        // Then by type + name, for levels made from routine rows
        var unlinked = rows.Where(l => l.UptickId == null && l.ExternalId == null)
            .GroupBy(l => (l.RoutineServiceTypeId, l.Name.Trim().ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var raw in batch.Where(r => !map.ContainsKey(r.Id)))
            if (raw.Rel("routineservicetype") is { } type && _types.TryGetValue(type, out var typeId) && raw.Text("name") is { } name
                && unlinked.Remove((typeId, name.ToUpperInvariant()), out var level))
                map[raw.Id] = level;
        return map;
    }

    protected override RoutineServiceLevel Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(RoutineServiceLevel l, UptickRaw raw)
    {
        var type = raw.Rel("routineservicetype");
        if (type == null) throw new UptickMapException("It has no routine service type.");
        if (!_types.TryGetValue(type.Value, out var typeId))
            throw new UptickMapException($"Its routine service type ({type}) isn't in FyreApp.");

        l.UptickId = raw.Id;
        l.ExternalId = raw.Id.ToString();
        l.RoutineServiceTypeId = typeId;
        l.Name = raw.Text("name", 100) ?? $"Level {raw.Id}";
        l.DisplayCode = raw.Text("display_code", 20);
        l.IntervalMonths = RoutineServiceLevelImporter.IntervalMonths(raw.Int("interval"), raw.Text("frequency")) ?? 0;
        l.OffsetMonths = raw.Int("offset");
        l.ToleranceInterval = raw.Int("tolerance_interval");
        l.ToleranceUnit = raw.Text("tolerance_unit", 10);
        l.DefaultEnabled = raw.Bool("is_default_enabled") ?? false;
        l.AssetBased = raw.Bool("is_asset_based") ?? false;
        l.SupersedesLowerRank = raw.Bool("supersedes_lower_rank") ?? false;
        l.ServiceRank = raw.Int("service_rank");
        l.Custom = raw.Bool("is_custom") ?? false;
        l.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(RoutineServiceLevel l) => l.Active = false;
}

// Uptick's remarktypes -> RemarkType ("1.02 - Battery missing indication"), with its asset type. Remarks a file
// import brought in are then linked to their type, as after a remark types export.
public class RemarkTypeMapper : UptickEntityMapper<RemarkType>
{
    private Dictionary<long, int> _assetTypes = new();

    public RemarkTypeMapper(AppDbContext db) : base(db) { }

    public override string Resource => "remarktypes";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _assetTypes = (await Db.AssetTypes.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
    }

    protected override async Task<Dictionary<long, RemarkType>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.RemarkTypes
            .Where(t => (t.UptickId != null && ids.Contains(t.UptickId.Value)) || (t.ExternalId != null && texts.Contains(t.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), t => t.UptickId, t => t.ExternalId);
    }

    protected override RemarkType Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(RemarkType t, UptickRaw raw)
    {
        t.UptickId = raw.Id;
        t.ExternalId = raw.Id.ToString();
        t.Label = raw.Text("label", 200) ?? $"Remark type {raw.Id}";
        t.Description = raw.Text("description");
        t.AssetTypeId = raw.Rel("type") is { } type && _assetTypes.TryGetValue(type, out var assetTypeId) ? assetTypeId : null;
        t.SeverityLabel = RemarkSeverity.Label(raw.Int("severity")) ?? t.SeverityLabel;
        t.Resolution = raw.Text("resolution");
        t.OwnerResponsible = raw.Bool("is_owner_responsible") ?? false;
        t.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(RemarkType t) => t.Active = false;

    // Remarks from the remarks export carry their type's Uptick id in their row
    protected override async Task AfterAsync(CancellationToken ct)
    {
        var idsByExt = await Db.RemarkTypes.AsNoTracking().Where(r => r.ExternalId != null)
            .ToDictionaryAsync(r => r.ExternalId!, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var remark in await Db.Remarks.Where(r => r.RemarkTypeId == null && r.UptickData != null).ToListAsync(ct))
            if (RemarkTypeImporter.RemarkTypeIdIn(remark.UptickData) is { } ext && idsByExt.TryGetValue(ext, out var id))
                remark.RemarkTypeId = id;
        await Db.SaveChangesAsync(ct);
    }
}
