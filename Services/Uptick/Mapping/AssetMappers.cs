using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's assettypes -> AssetType. A type the assets export created by name (no id) is claimed by its name.
public class AssetTypeMapper : UptickEntityMapper<AssetType>
{
    public AssetTypeMapper(AppDbContext db) : base(db) { }

    public override string Resource => "assettypes";

    protected override async Task<Dictionary<long, AssetType>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.AssetTypes
            .Where(t => (t.UptickId != null && ids.Contains(t.UptickId.Value)) || (t.ExternalId != null && texts.Contains(t.ExternalId))
                || (t.UptickId == null && t.ExternalId == null))
            .ToListAsync(ct);
        var map = Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), t => t.UptickId, t => t.ExternalId);

        // Then by name, for types made from asset rows
        var unlinked = rows.Where(t => t.UptickId == null && t.ExternalId == null)
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var raw in batch.Where(r => !map.ContainsKey(r.Id)))
            if (raw.Text("name") is { } name && unlinked.Remove(name, out var type))
                map[raw.Id] = type;
        return map;
    }

    protected override AssetType Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(AssetType t, UptickRaw raw)
    {
        t.UptickId = raw.Id;
        t.ExternalId = raw.Id.ToString();
        t.Name = raw.Text("name") ?? $"Asset type {raw.Id}";
        t.Category = raw.Text("category", 200);
        t.InspectionCriteria = raw.Text("inspection_criteria", 200);
        t.SequenceGroup = raw.Text("sequence_group", 100);
        t.Description = raw.Text("description");
        t.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(AssetType t) => t.Active = false;
}

// Uptick's assettypevariants -> AssetTypeVariant, under the type mapped from the variant's type. One the variants
// export made is matched by its id, or else by type + name.
public class AssetTypeVariantMapper : UptickEntityMapper<AssetTypeVariant>
{
    private Dictionary<long, int> _types = new();

    // Product id -> name, for the default replacement product
    private Dictionary<long, string> _products = new();

    public AssetTypeVariantMapper(AppDbContext db) : base(db) { }

    public override string Resource => "assettypevariants";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _types = (await Db.AssetTypes.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);

        _products = new Dictionary<long, string>();
        var products = await Db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == "products" && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Attributes })
            .ToListAsync(ct);
        foreach (var p in products)
        {
            using var a = JsonDocument.Parse(p.Attributes);
            if (long.TryParse(p.UptickId, out var id) && UptickRaw.Str(a.RootElement, "name")?.Trim() is { Length: > 0 } name)
                _products[id] = name;
        }
    }

    protected override async Task<Dictionary<long, AssetTypeVariant>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.AssetTypeVariants
            .Where(v => (v.UptickId != null && ids.Contains(v.UptickId.Value)) || (v.ExternalId != null && texts.Contains(v.ExternalId))
                || (v.UptickId == null && v.ExternalId == null))
            .ToListAsync(ct);
        var map = Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), v => v.UptickId, v => v.ExternalId);

        var unlinked = rows.Where(v => v.UptickId == null && v.ExternalId == null)
            .GroupBy(v => (v.AssetTypeId, v.Name.Trim().ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var raw in batch.Where(r => !map.ContainsKey(r.Id)))
            if (raw.Rel("type") is { } type && _types.TryGetValue(type, out var typeId) && raw.Text("name") is { } name
                && unlinked.Remove((typeId, name.ToUpperInvariant()), out var variant))
                map[raw.Id] = variant;
        return map;
    }

    protected override AssetTypeVariant Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(AssetTypeVariant v, UptickRaw raw)
    {
        var type = raw.Rel("type");
        if (type == null) throw new UptickMapException("It has no asset type.");
        if (!_types.TryGetValue(type.Value, out var typeId))
            throw new UptickMapException($"Its asset type ({type}) isn't in FyreApp.");

        v.UptickId = raw.Id;
        v.ExternalId = raw.Id.ToString();
        v.AssetTypeId = typeId;
        v.Name = raw.Text("name", 200) ?? $"Variant {raw.Id}";
        var product = raw.Rel("default_replacement_product") is { } p ? _products.GetValueOrDefault(p) : null;
        v.DefaultReplacementProduct = product?.Length > 200 ? product[..200] : product;
        v.Active = raw.Bool("is_active") ?? true;
    }

    protected override void Deactivate(AssetTypeVariant v) => v.Active = false;
}

// Uptick's assets -> Asset, at the property mapped from the asset's property, with its type, variant and who last
// serviced it. An asset the assets export placed (ExternalId = its id) is matched and updated.
public class AssetMapper : UptickEntityMapper<Asset>
{
    private Dictionary<long, int> _properties = new();
    private Dictionary<long, (int Id, string Name)> _variants = new();
    private Dictionary<long, int> _users = new();

    // Tracked for the batch, to set the asset's type
    private Dictionary<long, AssetType> _types = new();

    public AssetMapper(AppDbContext db) : base(db) { }

    public override string Resource => "assets";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Id);
        _variants = (await Db.AssetTypeVariants.AsNoTracking().Where(v => v.UptickId != null)
                .Select(v => new { UptickId = v.UptickId!.Value, v.Id, v.Name }).ToListAsync(ct))
            .ToDictionary(v => v.UptickId, v => (v.Id, v.Name));
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Id }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => u.Id);
    }

    protected override async Task<Dictionary<long, Asset>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        _types = (await Db.AssetTypes.Where(t => t.UptickId != null).ToListAsync(ct)).ToDictionary(t => t.UptickId!.Value);

        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.Assets.Include(a => a.AssetTypes)
            .Where(a => (a.UptickId != null && ids.Contains(a.UptickId.Value)) || (a.ExternalId != null && texts.Contains(a.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), a => a.UptickId, a => a.ExternalId);
    }

    protected override Asset Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Asset a, UptickRaw raw)
    {
        var property = raw.Rel("property");
        if (property == null) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(property.Value, out var propertyId))
            throw new UptickMapException($"Its property ({property}) isn't in FyreApp.");
        var typeId = raw.Rel("type");
        AssetType? type = null;
        if (typeId != null && !_types.TryGetValue(typeId.Value, out type))
            throw new UptickMapException($"Its asset type ({typeId}) isn't in FyreApp.");
        (int Id, string Name)? variant = raw.Rel("variant") is { } v && _variants.TryGetValue(v, out var found) ? found : null;

        a.UptickId = raw.Id;
        a.ExternalId = raw.Id.ToString();
        a.PropertyId = propertyId;
        a.Ref = raw.Text("ref");
        a.Name = raw.Text("label")
            ?? (string.Join(" ", new[] { type?.Name, variant?.Name }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } n ? n : null)
            ?? $"Asset {a.Ref ?? raw.Id.ToString()}";
        a.Location = raw.Text("location");
        a.Barcode = raw.Text("barcode");
        a.Make = raw.Text("make");
        a.Model = raw.Text("model");
        a.Size = raw.Text("size");
        a.Serial = raw.Text("inspection_ref", 100);
        a.Quantity = UptickRaw.Get(raw.A, "quantity") is { ValueKind: JsonValueKind.Number } q ? q.GetRawText() : raw.Text("quantity", 100);
        a.Compliance = Compliance(raw);
        a.IsActive = raw.Bool("is_active") ?? true;
        a.BaseDate = raw.Date("base_date");
        a.InstallationDate = raw.Date("installation_date");
        a.LastServiceDate = raw.Date("inspected_date");
        a.InactiveDate = raw.Time("inactive_date");
        a.InternalNote = raw.Text("internal_note");
        a.InspectionOrder = raw.Int("inspection_order");
        a.LastServicedById = raw.Rel("serviced_by") is { } user && _users.TryGetValue(user, out var userId) ? userId : null;

        // Uptick has one type per asset
        if (type != null && !a.AssetTypes.Contains(type))
        {
            a.AssetTypes.Clear();
            a.AssetTypes.Add(type);
        }
        a.Variant = variant?.Name;
        a.AssetTypeVariantId = variant?.Id;
    }

    // As the assets export wrote "Compliant": the last service's result
    private static string? Compliance(UptickRaw raw) => raw.Text("last_service_result") switch
    {
        "P" => "Pass",
        "F" => "Fail",
        "N" => "N/A",
        _ => raw.Bool("compliant") switch { true => "Pass", false => "Fail", null => null }
    };

    protected override void Deactivate(Asset a) => a.IsActive = false;

    // The type is held in the AssetAssetTypes join rather than a column
    protected override string? UptickValue(Asset a, LocalEdit edit) =>
        edit.Field == LocalEditLog.AssetTypes ? LocalEditLog.Text(a.AssetTypes.FirstOrDefault()?.Id) : base.UptickValue(a, edit);

    protected override void Keep(Asset a, LocalEdit edit)
    {
        if (edit.Field != LocalEditLog.AssetTypes)
        {
            base.Keep(a, edit);
            return;
        }
        var keep = edit.FyreAppValue == null ? (int?)null : int.Parse(edit.FyreAppValue, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var other in a.AssetTypes.Where(t => t.Id != keep).ToList()) a.AssetTypes.Remove(other);
        Db.ChangeTracker.DetectChanges();

        // Mapping just removed FyreApp's type: its join row is marked deleted, so un-delete it rather than adding
        // the type again (which EF leaves deleted)
        var join = Db.ChangeTracker.Entries().FirstOrDefault(e => e.Metadata.Name == "AssetAssetType" && e.State == EntityState.Deleted
            && (int)e.Property("AssetsId").CurrentValue! == a.Id && (int?)e.Property("AssetTypesId").CurrentValue == keep);
        if (join != null) join.State = EntityState.Unchanged;
        else if (keep != null && !a.AssetTypes.Any(t => t.Id == keep) && Db.AssetTypes.Find(keep.Value) is { } type) a.AssetTypes.Add(type);
    }
}

// Uptick's assettypes' to-many links (copied as "assettypes.links") -> which routine service types an asset type is
// serviced under and which of their levels it's left out of. Mapped after the routine service types and levels.
public class AssetTypeRoutineLinksMapper : IUptickMapper
{
    private readonly AppDbContext _db;
    private Dictionary<long, int> _routineTypes = new();
    private Dictionary<long, int> _levels = new();

    public AssetTypeRoutineLinksMapper(AppDbContext db) => _db = db;

    public string Resource => "assettypes" + Migration.UptickEndpoints.LinksSuffix;

    public async Task BeginAsync(CancellationToken ct)
    {
        _routineTypes = (await _db.RoutineServiceTypes.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
        _levels = (await _db.RoutineServiceLevels.AsNoTracking().Where(l => l.UptickId != null)
                .Select(l => new { UptickId = l.UptickId!.Value, l.Id }).ToListAsync(ct))
            .ToDictionary(l => l.UptickId, l => l.Id);
    }

    public async Task<IReadOnlyDictionary<string, string>> MapAsync(IReadOnlyList<UptickRecord> batch, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>();
        var ids = batch.Select(r => long.TryParse(r.UptickId, out var id) ? id : (long?)null).OfType<long>().ToList();
        var types = await _db.AssetTypes.Include(t => t.ApplicableRoutineTypes).Include(t => t.ExcludedRoutineLevels)
            .Where(t => t.UptickId != null && ids.Contains(t.UptickId.Value))
            .ToDictionaryAsync(t => t.UptickId!.Value, ct);
        var routineTypes = await _db.RoutineServiceTypes.Where(t => _routineTypes.Values.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var levels = await _db.RoutineServiceLevels.Where(l => _levels.Values.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        foreach (var record in batch)
        {
            try
            {
                if (!long.TryParse(record.UptickId, out var id)) throw new UptickMapException("The id isn't a number.");
                if (!types.TryGetValue(id, out var type)) throw new UptickMapException($"Its asset type ({id}) isn't in FyreApp.");
                var raw = UptickRaw.Parse(record, id);
                if (raw.Deleted)
                {
                    type.ApplicableRoutineTypes.Clear();
                    type.ExcludedRoutineLevels.Clear();
                    continue;
                }

                var applicable = Ids(raw.R, "applicable_routines")
                    .Select(r => _routineTypes.TryGetValue(r, out var t) ? routineTypes[t] : throw new UptickMapException($"Its routine service type ({r}) isn't in FyreApp."))
                    .ToList();
                var excluded = Ids(raw.R, "excluded_routinelevels")
                    .Select(l => _levels.TryGetValue(l, out var v) ? levels[v] : throw new UptickMapException($"Its routine level ({l}) isn't in FyreApp."))
                    .ToList();
                Replace(type.ApplicableRoutineTypes, applicable);
                Replace(type.ExcludedRoutineLevels, excluded);
            }
            catch (Exception ex) when (ex is UptickMapException or JsonException)
            {
                failures[record.UptickId] = ex.Message;
            }
        }

        _db.RecordLocalEdits = false;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _db.RecordLocalEdits = true;
        }
        return failures;
    }

    // A to-many relationship's ids: "applicable_routines": {"data": [{"type": "RoutineServiceType", "id": "18"}]}
    internal static IEnumerable<long> Ids(JsonElement relationships, string name) =>
        UptickRaw.Get(relationships, name) is { ValueKind: JsonValueKind.Object } rel && rel.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
                .Select(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var i) && long.TryParse(i.ToString(), out var n) ? n : (long?)null)
                .OfType<long>()
                .Distinct()
                .ToList()
            : [];

    private static void Replace<T>(ICollection<T> current, IReadOnlyCollection<T> wanted)
    {
        foreach (var gone in current.Where(c => !wanted.Contains(c)).ToList()) current.Remove(gone);
        foreach (var added in wanted.Where(w => !current.Contains(w))) current.Add(added);
    }
}
