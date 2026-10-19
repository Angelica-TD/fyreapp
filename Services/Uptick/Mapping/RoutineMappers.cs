using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's routineservices -> RoutineService: a routine type set up at a property, from its start date. (Uptick
// owns its forecast: its routines are mapped too, so FyreApp doesn't forecast these, and they aren't edited here.)
public class RoutineServiceMapper : UptickEntityMapper<RoutineService>
{
    private Dictionary<long, int> _properties = new();
    private Dictionary<long, int> _types = new();
    private Dictionary<long, string> _contractors = new();

    public RoutineServiceMapper(AppDbContext db) : base(db) { }

    public override string Resource => "routineservices";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => p.Id);
        _types = (await Db.RoutineServiceTypes.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
        _contractors = await RoutineServiceTypeMapper.RawNamesAsync(Db, "contractors", ct);

        // Before tasks are mapped, so routine tasks link to them
        await (await RoutinePlaces.LoadAsync(Db, ct)).EnsureSchedulesAsync(ct);
    }

    protected override async Task<Dictionary<long, RoutineService>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var map = await Db.RoutineServices.Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value)).ToDictionaryAsync(s => s.UptickId!.Value, ct);

        // One mapped before that lost its id (the migration rolled back and applied again): the same property and type
        var unlinked = (await Db.RoutineServices.Where(s => s.UptickId == null && s.CreatedBy == "Uptick").ToListAsync(ct))
            .GroupBy(s => (s.PropertyId, s.RoutineServiceTypeId))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToList());
        foreach (var raw in batch.Where(r => !map.ContainsKey(r.Id)))
            if (raw.Rel("property") is { } p && _properties.TryGetValue(p, out var property) && raw.Rel("type") is { } t
                && _types.TryGetValue(t, out var type) && unlinked.TryGetValue((property, type), out var claimable) && claimable.Count > 0)
            {
                map[raw.Id] = claimable[0];
                claimable.RemoveAt(0);
            }
        return map;
    }

    protected override RoutineService Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(RoutineService s, UptickRaw raw)
    {
        if (raw.Rel("property") is not { } propertyId) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(propertyId, out var property)) throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");
        if (raw.Rel("type") is not { } typeId) throw new UptickMapException("It has no routine service type.");
        if (!_types.TryGetValue(typeId, out var type)) throw new UptickMapException($"Its routine service type ({typeId}) isn't in FyreApp.");

        var created = raw.Time("created");
        s.UptickId = raw.Id;
        s.PropertyId = property;
        s.RoutineServiceTypeId = type;
        s.StartDate = raw.Date("start_date") ?? Day(created) ?? Day(DateTime.UtcNow)!.Value;
        s.LastAnnualDate = raw.Date("annual_test_date") ?? s.StartDate;
        // An inactive one ended when it was last changed
        s.EndDate = raw.Date("end_date") ?? (raw.Bool("is_active") == false ? Day(raw.Time("updated")) : null);
        s.Subcontractor = RoutineServiceTypeMapper.Cap(raw.Rel("contractor") is { } c ? _contractors.GetValueOrDefault(c) : null, 200);
        s.CreatedUtc = created ?? s.CreatedUtc;
        s.CreatedBy = "Uptick";
    }

    // Removing one would remove its routines too; it ends instead
    protected override void Deactivate(RoutineService s) => s.EndDate ??= Day(DateTime.UtcNow);

    private static DateTime? Day(DateTime? utc) => utc is { } t ? DateTime.SpecifyKind(t.Date, DateTimeKind.Utc) : null;
}

// Uptick's routineservicelevels: the levels (Six-monthly, Annual) done for a routine service at its property. They're
// the routine service's Levels in FyreApp; an inactive or deleted one is taken off.
public class RoutineServiceLevelLinkMapper : IUptickMapper
{
    private readonly AppDbContext _db;
    private Dictionary<long, int> _levels = new();

    public RoutineServiceLevelLinkMapper(AppDbContext db) => _db = db;

    public string Resource => "routineservicelevels";

    public async Task BeginAsync(CancellationToken ct)
    {
        _levels = (await _db.RoutineServiceLevels.AsNoTracking().Where(l => l.UptickId != null)
                .Select(l => new { UptickId = l.UptickId!.Value, l.Id }).ToListAsync(ct))
            .ToDictionary(l => l.UptickId, l => l.Id);
    }

    public async Task<IReadOnlyDictionary<string, string>> MapAsync(IReadOnlyList<UptickRecord> batch, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>();
        var links = new List<(string Id, long Service, int Level, bool On)>();
        foreach (var record in batch)
        {
            try
            {
                if (!long.TryParse(record.UptickId, out var id)) throw new UptickMapException("The id isn't a number.");
                var raw = UptickRaw.Parse(record, id);
                if (raw.Rel("routineservice") is not { } service) throw new UptickMapException("It has no routine service.");
                if (raw.Rel("type") is not { } type) throw new UptickMapException("It has no level.");
                if (!_levels.TryGetValue(type, out var level)) throw new UptickMapException($"Its level ({type}) isn't in FyreApp.");
                links.Add((record.UptickId, service, level, !raw.Deleted && raw.Bool("is_active") != false));
            }
            catch (Exception ex) when (ex is UptickMapException or JsonException)
            {
                failures[record.UptickId] = ex.Message;
            }
        }

        var serviceIds = links.Select(l => l.Service).Distinct().ToList();
        var services = await _db.RoutineServices.Include(s => s.Levels)
            .Where(s => s.UptickId != null && serviceIds.Contains(s.UptickId.Value))
            .ToDictionaryAsync(s => s.UptickId!.Value, ct);
        var levelIds = links.Select(l => l.Level).Distinct().ToList();
        var levels = await _db.RoutineServiceLevels.Where(l => levelIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        // A level on and off in the same batch (one record replaced by another) ends up on
        foreach (var (id, serviceId, levelId, on) in links.OrderBy(l => l.On))
        {
            if (!services.TryGetValue(serviceId, out var service))
            {
                failures[id] = $"Its routine service ({serviceId}) isn't in FyreApp.";
                continue;
            }
            var level = levels[levelId];
            if (on && !service.Levels.Contains(level)) service.Levels.Add(level);
            else if (!on) service.Levels.Remove(level);
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
}

// Uptick's routines -> Routine: one due occurrence of a routine service's level, e.g. "10 - Portable and Wheeled Fire
// Extinguishers: Six-monthly" due 31 Oct 2026, with its status and the task done for it (from the service tasks done
// for it). As the routines export did, each is linked to its property's maintenance schedule for that interval,
// created from the routines when the property has none. A routine the routines export placed is matched by its id.
public class RoutineMapper : UptickEntityMapper<Routine>
{
    private RoutinePlaces _places = null!;
    private Dictionary<long, int> _routineServices = new();
    private Dictionary<long, int> _tasks = new();

    public RoutineMapper(AppDbContext db) : base(db) { }

    public override string Resource => "routines";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _places = await RoutinePlaces.LoadAsync(Db, ct);
        await _places.EnsureSchedulesAsync(ct);
        _routineServices = (await Db.RoutineServices.AsNoTracking().Where(s => s.UptickId != null)
                .Select(s => new { UptickId = s.UptickId!.Value, s.Id }).ToListAsync(ct))
            .ToDictionary(s => s.UptickId, s => s.Id);
        // The task a routine was done on: the latest one with a service task done for it
        _tasks = (await Db.ServiceTasks.AsNoTracking().Where(s => s.UptickRoutineId != null)
                .Select(s => new { Routine = s.UptickRoutineId!.Value, s.ClientTaskId, s.Id }).ToListAsync(ct))
            .GroupBy(s => s.Routine)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Id).First().ClientTaskId);
    }

    protected override async Task<Dictionary<long, Routine>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.Routines
            .Where(r => (r.UptickId != null && ids.Contains(r.UptickId.Value)) || (r.ExternalId != null && texts.Contains(r.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), r => r.UptickId, r => r.ExternalId);
    }

    protected override Routine Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Routine r, UptickRaw raw)
    {
        if (_places.Place(raw.Rel("routineservicelevel")) is not { } place)
            throw new UptickMapException($"Its routine service level ({raw.Rel("routineservicelevel")}) or its property isn't in FyreApp.");
        if (raw.Date("due") is not { } due) throw new UptickMapException("It has no due date.");

        var name = raw.Text("display_name") ?? "";
        r.UptickId = raw.Id;
        r.ExternalId = raw.Id.ToString();
        r.PropertyId = place.PropertyId;
        r.Name = name.Length > 300 ? name[..300] : name;
        r.RoutineServiceLevelId = place.Level?.Id;
        r.RoutineServiceId = _routineServices.TryGetValue(place.Service, out var service) ? service : null;
        r.MaintenanceScheduleId = place.Level is { Months: > 0 } level && _places.Schedules.TryGetValue((place.PropertyId, level.Months), out var schedule)
            ? schedule : null;
        r.DueDate = due;
        r.ToleranceStart = raw.Date("tolerance_period_start");
        r.ToleranceEnd = raw.Date("tolerance_period_end");
        r.CompletedDate = raw.Date("completed_date") ?? raw.Time("completed_date");
        r.Status = ScheduleImportService.OccurrenceStatus(raw.Text("status"), r.CompletedDate?.ToString("O"));
        r.ClientTaskId = _tasks.TryGetValue(raw.Id, out var task) ? task : r.ClientTaskId;
        r.ServiceGroup = RoutineServiceTypeMapper.Cap(place.Group is { } g ? _places.Groups.GetValueOrDefault(g) : null, 100);
        r.Subcontractor = RoutineServiceTypeMapper.Cap(place.Contractor is { } c ? _places.Contractors.GetValueOrDefault(c) : null, 200);
        r.EstimatedMinutes = ScheduleImportService.ParseMinutes(place.Duration) ?? r.EstimatedMinutes;
    }

    // Gone from Uptick: cancelled, so it's no longer due
    protected override void Deactivate(Routine r) => r.Status = RoutineStatus.Cancelled;
}

// Where each Uptick routine is (through its routine service level and routine service, both only read from the raw
// copy here) and the maintenance schedules its property has for its interval
public class RoutinePlaces
{
    private readonly AppDbContext _db;
    // Uptick routine service level id -> (routine service, level type, service group, contractor, duration)
    private Dictionary<long, (long Service, long? Level, long? Group, long? Contractor, string? Duration)> _serviceLevels = new();
    // Uptick routine service id -> (property, service group, contractor)
    private Dictionary<long, (long? Property, long? Group, long? Contractor)> _services = new();
    private Dictionary<long, int> _properties = new();
    private Dictionary<long, (int Id, int Months)> _levels = new();

    private RoutinePlaces(AppDbContext db) => _db = db;

    public Dictionary<long, string> Groups { get; private set; } = new();
    public Dictionary<long, string> Contractors { get; private set; } = new();
    public Dictionary<(int PropertyId, int Months), int> Schedules { get; private set; } = new();

    public static async Task<RoutinePlaces> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var p = new RoutinePlaces(db);
        p._serviceLevels = (await RemarkMapper.RawAsync(db, "routineservicelevels", ct)).Where(l => RemarkMapper.Rel(l.R, "routineservice") != null)
            .ToDictionary(l => l.Id, l => (RemarkMapper.Rel(l.R, "routineservice")!.Value, RemarkMapper.Rel(l.R, "type"),
                RemarkMapper.Rel(l.R, "servicegroup"), RemarkMapper.Rel(l.R, "contractor"), UptickRaw.Str(l.A, "estimated_duration")));
        p._services = (await RemarkMapper.RawAsync(db, "routineservices", ct))
            .ToDictionary(s => s.Id, s => (RemarkMapper.Rel(s.R, "property"), RemarkMapper.Rel(s.R, "servicegroup"), RemarkMapper.Rel(s.R, "contractor")));
        p._properties = (await db.Properties.AsNoTracking().Where(x => x.UptickId != null)
                .Select(x => new { UptickId = x.UptickId!.Value, x.Id }).ToListAsync(ct))
            .ToDictionary(x => x.UptickId, x => x.Id);
        p._levels = (await db.RoutineServiceLevels.AsNoTracking().Where(l => l.UptickId != null)
                .Select(l => new { UptickId = l.UptickId!.Value, l.Id, l.IntervalMonths }).ToListAsync(ct))
            .ToDictionary(l => l.UptickId, l => (l.Id, l.IntervalMonths));
        p.Groups = await RoutineServiceTypeMapper.RawNamesAsync(db, "servicegroups", ct);
        p.Contractors = await RoutineServiceTypeMapper.RawNamesAsync(db, "contractors", ct);
        return p;
    }

    // Where a routine is, through its routine service level and routine service
    public (int PropertyId, long Service, (int Id, int Months)? Level, long? Group, long? Contractor, string? Duration)? Place(long? serviceLevelId)
    {
        if (serviceLevelId is not { } sl || !_serviceLevels.TryGetValue(sl, out var level)) return null;
        if (!_services.TryGetValue(level.Service, out var service) || service.Property is not { } p || !_properties.TryGetValue(p, out var propertyId))
            return null;
        (int Id, int Months)? type = level.Level is { } t && _levels.TryGetValue(t, out var found) ? found : null;
        return (propertyId, level.Service, type, level.Group ?? service.Group, level.Contractor ?? service.Contractor, level.Duration);
    }

    // One schedule per (property, interval) the routines run at, as the routines export made them: next run the
    // earliest outstanding routine. Existing schedules are left as they are, but take any routine levels they lack.
    public async Task EnsureSchedulesAsync(CancellationToken ct)
    {
        var groups = new Dictionary<(int PropertyId, int Months), (List<(string Routine, DateTime Due, bool Done, bool TaskRaised)> Occurrences, HashSet<int> Levels, string Label)>();
        foreach (var r in await RemarkMapper.RawAsync(_db, "routines", ct))
        {
            if (Place(RemarkMapper.Rel(r.R, "routineservicelevel")) is not { Level: { Months: > 0 } level } place) continue;
            if (UptickRaw.Str(r.A, "due") is not { } dueText || !DateTime.TryParse(dueText, out var due)) continue;
            var name = UptickRaw.Str(r.A, "display_name") ?? "";
            var status = UptickRaw.Str(r.A, "status");
            var key = (place.PropertyId, level.Months);
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = (new(), new(), ScheduleImportService.ParseFrequency(name).Label);
            g.Occurrences.Add((name, DateTime.SpecifyKind(due.Date, DateTimeKind.Utc),
                status is "C" or "X" || UptickRaw.Str(r.A, "completed_date") != null, status == "G"));
            g.Levels.Add(level.Id);
        }

        var propertyIds = groups.Keys.Select(k => k.PropertyId).Distinct().ToList();
        var existing = (await _db.MaintenanceSchedules.Include(s => s.MaintenanceInterval).Include(s => s.RoutineLevels)
                .Where(s => s.TargetType == ScheduleTargetType.Property && s.PropertyId != null && propertyIds.Contains(s.PropertyId.Value))
                .ToListAsync(ct))
            .GroupBy(s => (s.PropertyId!.Value, s.MaintenanceInterval.Months))
            .ToDictionary(g => g.Key, g => g.First());
        var intervals = await _db.MaintenanceIntervals.ToListAsync(ct);
        var levels = await _db.RoutineServiceLevels.Where(l => l.UptickId != null).ToDictionaryAsync(l => l.Id, ct);

        MaintenanceInterval IntervalFor(int months, string label)
        {
            var match = intervals.Where(i => i.Months == months).OrderBy(i => i.Id).FirstOrDefault();
            if (match != null) return match;
            var name = intervals.Any(i => string.Equals(i.Name, label, StringComparison.OrdinalIgnoreCase)) ? $"{label} ({months} months)" : label;
            var created = new MaintenanceInterval { Name = name, Months = months };
            intervals.Add(created);
            return created;
        }

        var now = DateTime.UtcNow;
        foreach (var (key, group) in groups)
        {
            if (!existing.TryGetValue(key, out var schedule))
            {
                var next = ScheduleImportService.NextOutstanding(group.Occurrences, now) ?? group.Occurrences.Max(o => o.Due).AddMonths(key.Months);
                schedule = new MaintenanceSchedule
                {
                    TargetType = ScheduleTargetType.Property,
                    PropertyId = key.PropertyId,
                    MaintenanceInterval = IntervalFor(key.Months, group.Label),
                    StartDate = next.AddMonths(-key.Months),
                    NextRunDate = next,
                    IsActive = true
                };
                _db.MaintenanceSchedules.Add(schedule);
                existing[key] = schedule;
            }
            foreach (var id in group.Levels)
                if (levels.TryGetValue(id, out var level) && !schedule.RoutineLevels.Contains(level))
                    schedule.RoutineLevels.Add(level);
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
        Schedules = existing.ToDictionary(e => e.Key, e => e.Value.Id);
    }

}
