using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's tasks -> ClientTask, at the property mapped from the task's property and assigned to the Uptick user it's
// assigned to. As the export did, a routine (I&T) task is linked to its property's schedules for the frequencies in
// its scope (a completed one rolls them forward).
public class TaskMapper : UptickEntityMapper<ClientTask>
{
    private Dictionary<long, (int Id, int ClientId)> _properties = new();
    private Dictionary<long, int> _clients = new();
    private Dictionary<long, (int Id, string Name)> _users = new();
    private Dictionary<long, string> _categories = new();

    // For the batch: schedules (tracked, to link and roll forward), the no-property placeholder, and the tasks mapped
    private ILookup<(int PropertyId, int Months), MaintenanceSchedule> _schedules = Enumerable.Empty<MaintenanceSchedule>().ToLookup(s => (0, 0));
    private (int Id, int ClientId)? _noProperty;
    private readonly List<ClientTask> _mapped = new();

    public TaskMapper(AppDbContext db) : base(db) { }

    public override string Resource => "tasks";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _properties = (await Db.Properties.AsNoTracking().Where(p => p.UptickId != null)
                .Select(p => new { UptickId = p.UptickId!.Value, p.Id, p.ClientId }).ToListAsync(ct))
            .ToDictionary(p => p.UptickId, p => (p.Id, p.ClientId));
        _clients = (await Db.Clients.AsNoTracking().Where(c => c.UptickId != null)
                .Select(c => new { UptickId = c.UptickId!.Value, c.Id }).ToListAsync(ct))
            .ToDictionary(c => c.UptickId, c => c.Id);
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Id, u.Name }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => (u.Id, u.Name));
        _categories = await RoutineServiceTypeMapper.RawNamesAsync(Db, "taskcategories", ct);
    }

    protected override async Task<Dictionary<long, ClientTask>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        _mapped.Clear();
        _schedules = (await Db.MaintenanceSchedules
                .Include(s => s.MaintenanceInterval)
                .Where(s => s.TargetType == ScheduleTargetType.Property && s.PropertyId != null && s.IsActive)
                .ToListAsync(ct))
            .ToLookup(s => (s.PropertyId!.Value, s.MaintenanceInterval.Months));
        if (_noProperty == null && batch.Any(r => !r.Deleted && r.Rel("property") == null))
            _noProperty = await UptickTaskRules.GetNoPropertySiteAsync(Db, ct);

        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.ClientTasks.Include(t => t.CoveredSchedules)
            .Where(t => t.UptickId != null && ids.Contains(t.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, t => t.UptickId);
    }

    protected override ClientTask Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(ClientTask t, UptickRaw raw)
    {
        // A few Uptick tasks have no property; they're kept on one placeholder property, as the export did
        (int Id, int ClientId) property;
        if (raw.Rel("property") is not { } propertyId) property = _noProperty!.Value;
        else if (!_properties.TryGetValue(propertyId, out property))
            throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");

        var status = UptickTaskRules.MapStatus(raw.RelText("status"));
        var description = raw.Text("description");
        var scope = raw.Text("scope_of_works");
        var reference = raw.Text("ref", 20);

        t.UptickId = raw.Id;
        t.Ref = reference;
        t.PropertyId = property.Id;
        t.ClientId = raw.Rel("client") is { } client && _clients.TryGetValue(client, out var clientId) ? clientId : property.ClientId;
        t.Title = UptickTaskRules.Truncate(raw.Text("name") ?? UptickTaskRules.FirstLine(description) ?? reference ?? raw.Id.ToString(), 120);
        t.Description = UptickTaskRules.BuildDescription(description, scope);
        t.ScopeOfWorks = scope == null ? null : UptickTaskRules.Truncate(scope, 4000);
        t.Status = status;
        t.Priority = UptickTaskRules.MapPriority(raw.Int("priority"));
        t.DueDateUtc = raw.Date("due");
        t.CreatedUtc = raw.Time("created") ?? (t.CreatedUtc == default ? DateTime.UtcNow : t.CreatedUtc);
        t.ScheduledDate = raw.Date("scheduled_date") ?? LocalDay(raw.Text("scheduled_at"));
        t.PerformedDate = LocalDay(raw.Text("status_changed_inspected"));
        t.CompletedUtc = status == ClientTaskStatus.Completed ? raw.Time("status_changed_complete") ?? raw.Time("inactive_date") : null;
        t.Category = raw.Rel("category") is { } category ? _categories.GetValueOrDefault(category) : null;
        t.IsActive = raw.Bool("is_active") ?? status is not (ClientTaskStatus.Completed or ClientTaskStatus.Cancelled);

        // Who it's assigned to in Uptick; one FyreApp doesn't hold leaves any FyreApp assignment
        if ((raw.Rel("assigned_to") ?? raw.Rel("technician")) is { } user && _users.TryGetValue(user, out var assignee))
        {
            t.UptickTechnician = assignee.Name;
            t.AssignedToUptickUserId = assignee.Id;
        }
        else t.UptickTechnician = null;

        if (t.Category == "I&T")
            foreach (var s in UptickTaskRules.ScopeMonths(scope).SelectMany(m => _schedules[(property.Id, m)]).Distinct())
                if (!t.CoveredSchedules.Contains(s)) t.CoveredSchedules.Add(s);
        _mapped.Add(t);
    }

    // A date-time as the date it was where it happened (its own offset), midnight UTC as dates are kept
    private static DateTime? LocalDay(string? s) =>
        s != null && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t)
            ? DateTime.SpecifyKind(t.Date, DateTimeKind.Utc) : null;

    // A task deleted in Uptick: no longer open work
    protected override void Deactivate(ClientTask t) => t.IsActive = false;

    protected override async Task AfterAsync(CancellationToken ct)
    {
        if (UptickTaskRules.RollSchedulesForward(Db, _mapped) > 0) await Db.SaveChangesAsync(ct);
    }
}

// Uptick's servicetasks -> ServiceTask: an asset serviced on a task for a routine level, with the result. The task's
// Work tab lists them. A line of stock used has no asset.
public class ServiceTaskMapper : UptickEntityMapper<ServiceTask>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, int> _assets = new();
    private Dictionary<long, (int Id, string Routine)> _levels = new();
    private Dictionary<long, int> _users = new();

    public ServiceTaskMapper(AppDbContext db) : base(db) { }

    public override string Resource => "servicetasks";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = (await Db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
        _assets = (await Db.Assets.AsNoTracking().Where(a => a.UptickId != null)
                .Select(a => new { UptickId = a.UptickId!.Value, a.Id }).ToListAsync(ct))
            .ToDictionary(a => a.UptickId, a => a.Id);
        // "<routine service type>: <level>", as Uptick names a routine
        _levels = (await Db.RoutineServiceLevels.AsNoTracking().Where(l => l.UptickId != null)
                .Select(l => new { UptickId = l.UptickId!.Value, l.Id, Type = l.RoutineServiceType.Name, l.Name }).ToListAsync(ct))
            .ToDictionary(l => l.UptickId, l => (l.Id, $"{l.Type}: {l.Name}"));
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Id }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => u.Id);
    }

    protected override async Task<Dictionary<long, ServiceTask>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return await Db.ServiceTasks.Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value)).ToDictionaryAsync(s => s.UptickId!.Value, ct);
    }

    protected override ServiceTask Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(ServiceTask s, UptickRaw raw)
    {
        if (raw.Rel("task") is not { } task) throw new UptickMapException("It has no task.");
        if (!_tasks.TryGetValue(task, out var taskId)) throw new UptickMapException($"Its task ({task}) isn't in FyreApp.");
        int? assetId = null;
        if (raw.Rel("item") is { } asset)
        {
            if (!_assets.TryGetValue(asset, out var id)) throw new UptickMapException($"Its asset ({asset}) isn't in FyreApp.");
            assetId = id;
        }
        (int Id, string Routine)? level = raw.Rel("routineserviceleveltype") is { } l && _levels.TryGetValue(l, out var found) ? found : null;

        s.UptickId = raw.Id;
        s.ClientTaskId = taskId;
        s.AssetId = assetId;
        s.RoutineServiceLevelId = level?.Id;
        s.Routine = level?.Routine is { } routine ? (routine.Length > 300 ? routine[..300] : routine) : "";
        s.Result = raw.Text("result") switch { "P" => "Pass", "F" => "Fail", "N" => "N/A", _ => null };
        s.PerformedUtc = raw.Time("performed_date");
        s.TechnicianId = raw.Rel("technician") is { } user && _users.TryGetValue(user, out var userId) ? userId : null;
        s.Notes = raw.Text("notes");
        s.UptickRoutineId = raw.Rel("routine");
        s.UptickRemarkId = raw.Rel("remark");
        s.UptickProductId = raw.Rel("product");
    }

    // Gone from Uptick: nothing in FyreApp points at a service task, and the raw copy keeps it
    protected override void Deactivate(ServiceTask s) => Db.ServiceTasks.Remove(s);
}
