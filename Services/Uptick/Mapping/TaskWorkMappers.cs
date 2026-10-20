using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// A task's work in Uptick, shown on the task page: time logged (tasksessions), visits booked (appointments, with
// their technicians from appointments.links), the task's lines (subtasks) and stock taken (taskstocktransactions).
// Each is on its task; one deleted in Uptick is removed (nothing in FyreApp points at it).
internal static class TaskWorkLookups
{
    public static async Task<Dictionary<long, int>> TasksAsync(AppDbContext db, CancellationToken ct) =>
        (await db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
            .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
        .ToDictionary(t => t.UptickId, t => t.Id);

    public static async Task<Dictionary<long, int>> UsersAsync(AppDbContext db, CancellationToken ct) =>
        (await db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
            .Select(u => new { UptickId = u.UptickId!.Value, u.Id }).ToListAsync(ct))
        .ToDictionary(u => u.UptickId, u => u.Id);

    public static async Task<Dictionary<long, int>> ProductsAsync(AppDbContext db, CancellationToken ct) =>
        (await db.Products.AsNoTracking().Where(p => p.UptickId != null)
            .Select(p => new { UptickId = p.UptickId!.Value, p.Id }).ToListAsync(ct))
        .ToDictionary(p => p.UptickId, p => p.Id);

    // Id -> attributes of a small reference endpoint held only in the raw copy (session types, categories)
    public static async Task<Dictionary<long, JsonElement>> RawAsync(AppDbContext db, string resource, CancellationToken ct)
    {
        var map = new Dictionary<long, JsonElement>();
        foreach (var r in await db.UptickRecords.AsNoTracking().Where(r => r.Resource == resource && r.DeletedUtc == null)
                     .Select(r => new { r.UptickId, r.Attributes }).ToListAsync(ct))
            if (long.TryParse(r.UptickId, out var id))
            {
                using var doc = JsonDocument.Parse(r.Attributes);
                map[id] = doc.RootElement.Clone();
            }
        return map;
    }

    public static int Task(UptickRaw raw, Dictionary<long, int> tasks) =>
        raw.Rel("task") is not { } task ? throw new UptickMapException("It has no task.")
        : tasks.TryGetValue(task, out var id) ? id
        : throw new UptickMapException($"Its task ({task}) isn't in FyreApp.");
}

// Uptick's tasksessions -> TaskSession: who logged what time on a task, of which type (travelling, on site, …)
public class TaskSessionMapper : UptickEntityMapper<TaskSession>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, int> _users = new();
    private Dictionary<long, JsonElement> _types = new();

    public TaskSessionMapper(AppDbContext db) : base(db) { }

    public override string Resource => "tasksessions";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = await TaskWorkLookups.TasksAsync(Db, ct);
        _users = await TaskWorkLookups.UsersAsync(Db, ct);
        _types = await TaskWorkLookups.RawAsync(Db, "tasksessiontypes", ct);
    }

    protected override async Task<Dictionary<long, TaskSession>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.TaskSessions.Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value)).ToListAsync(ct), s => s.UptickId);
    }

    protected override TaskSession Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(TaskSession s, UptickRaw raw)
    {
        var task = TaskWorkLookups.Task(raw, _tasks);
        var started = raw.Time("started") ?? throw new UptickMapException("It has no start.");
        JsonElement? type = raw.Rel("type") is { } t && _types.TryGetValue(t, out var found) ? found : null;

        s.UptickId = raw.Id;
        s.ClientTaskId = task;
        s.UptickUserId = raw.Rel("technician") is { } u && _users.TryGetValue(u, out var user) ? user : null;
        s.Type = type is { } ty ? RoutineServiceTypeMapper.Cap(UptickRaw.Str(ty, "label"), 100) : null;
        s.IsOnsite = type is { } on && UptickRaw.Get(on, "is_onsite").ValueKind == JsonValueKind.True;
        s.StartedUtc = started;
        s.FinishedUtc = raw.Time("finished");
        s.Hours = raw.Decimal("hours");
        s.Cost = raw.Decimal("cost");
        s.Rate = raw.Text("multiplier_label", 100);
        s.Description = raw.Text("description");
        s.IsSubmitted = raw.Bool("is_submitted") ?? false;
        s.IsApproved = raw.Bool("is_approved") ?? false;
        s.StartedLatitude = UptickRaw.Double(raw.A, "started_coord_lat");
        s.StartedLongitude = UptickRaw.Double(raw.A, "started_coord_lng");
        s.FinishedLatitude = UptickRaw.Double(raw.A, "finished_coord_lat");
        s.FinishedLongitude = UptickRaw.Double(raw.A, "finished_coord_lng");
        s.TrashedUtc = raw.Time("trashed");
    }

    protected override void Deactivate(TaskSession s) => Db.TaskSessions.Remove(s);
}

// Uptick's appointments -> Appointment: a visit booked for a task. Its technicians come with appointments.links.
public class AppointmentMapper : UptickEntityMapper<Appointment>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, JsonElement> _categories = new();

    public AppointmentMapper(AppDbContext db) : base(db) { }

    public override string Resource => "appointments";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = await TaskWorkLookups.TasksAsync(Db, ct);
        _categories = await TaskWorkLookups.RawAsync(Db, "appointmentcategories", ct);
    }

    protected override async Task<Dictionary<long, Appointment>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.Appointments.Where(a => a.UptickId != null && ids.Contains(a.UptickId.Value)).ToListAsync(ct), a => a.UptickId);
    }

    protected override Appointment Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Appointment a, UptickRaw raw)
    {
        var task = TaskWorkLookups.Task(raw, _tasks);
        a.UptickId = raw.Id;
        a.ClientTaskId = task;
        a.StartUtc = raw.Time("scheduled_start") ?? throw new UptickMapException("It has no start.");
        a.EndUtc = raw.Time("scheduled_end");
        a.Status = raw.Text("status", 30);
        a.Subject = raw.Text("subject", 500);
        a.Notes = raw.Text("notes");
        a.Address = raw.Text("address", 500);
        a.Category = raw.Rel("category") is { } c && _categories.TryGetValue(c, out var category)
            ? RoutineServiceTypeMapper.Cap(UptickRaw.Str(category, "label"), 100) : null;
    }

    protected override void Deactivate(Appointment a) => Db.Appointments.Remove(a);
}

// appointments.links -> Appointment.Technicians: who's booked, a to-many link Uptick's list responses leave out
// (UptickEndpoints.WithLinks)
public class AppointmentTechniciansMapper : IUptickMapper
{
    private readonly AppDbContext _db;
    private Dictionary<long, int> _users = new();

    public AppointmentTechniciansMapper(AppDbContext db) => _db = db;

    public string Resource => "appointments" + Migration.UptickEndpoints.LinksSuffix;

    public async Task BeginAsync(CancellationToken ct) => _users = await TaskWorkLookups.UsersAsync(_db, ct);

    public async Task<IReadOnlyDictionary<string, string>> MapAsync(IReadOnlyList<UptickRecord> batch, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>();
        var ids = batch.Select(r => long.TryParse(r.UptickId, out var id) ? id : (long?)null).OfType<long>().ToList();
        var appointments = await _db.Appointments.Include(a => a.Technicians)
            .Where(a => a.UptickId != null && ids.Contains(a.UptickId.Value))
            .ToDictionaryAsync(a => a.UptickId!.Value, ct);
        var users = await _db.UptickUsers.Where(u => _users.Values.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        foreach (var record in batch)
        {
            try
            {
                if (!long.TryParse(record.UptickId, out var id)) throw new UptickMapException("The id isn't a number.");
                // An appointment FyreApp couldn't map (its task isn't held) is reported there
                if (!appointments.TryGetValue(id, out var appointment)) continue;
                var raw = UptickRaw.Parse(record, id);
                var wanted = raw.Deleted
                    ? []
                    : AssetTypeRoutineLinksMapper.Ids(raw.R, "technicians")
                        .Select(u => _users.TryGetValue(u, out var user) ? users[user] : throw new UptickMapException($"Its technician ({u}) isn't in FyreApp."))
                        .ToList();
                AssetTypeRoutineLinksMapper.Replace(appointment.Technicians, wanted);
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
}

// Uptick's subtasks -> Subtask: a task's lines, each a routine due or a product, done or not
public class SubtaskMapper : UptickEntityMapper<Subtask>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, int> _routines = new();
    private Dictionary<long, int> _products = new();

    public SubtaskMapper(AppDbContext db) : base(db) { }

    public override string Resource => "subtasks";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = await TaskWorkLookups.TasksAsync(Db, ct);
        _products = await TaskWorkLookups.ProductsAsync(Db, ct);
        _routines = (await Db.Routines.AsNoTracking().Where(r => r.UptickId != null)
                .Select(r => new { UptickId = r.UptickId!.Value, r.Id }).ToListAsync(ct))
            .ToDictionary(r => r.UptickId, r => r.Id);
    }

    protected override async Task<Dictionary<long, Subtask>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.Subtasks.Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value)).ToListAsync(ct), s => s.UptickId);
    }

    protected override Subtask Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Subtask s, UptickRaw raw)
    {
        s.UptickId = raw.Id;
        s.ClientTaskId = TaskWorkLookups.Task(raw, _tasks);
        s.Order = raw.Int("order") ?? 0;
        s.RoutineId = raw.Rel("routine") is { } r && _routines.TryGetValue(r, out var routine) ? routine : null;
        s.ProductId = raw.Rel("product") is { } p && _products.TryGetValue(p, out var product) ? product : null;
        s.IsPerformed = raw.Bool("is_performed") ?? false;
        s.PerformedDate = raw.Date("performed_date");
        s.IsServiceTask = raw.Bool("service_task") ?? false;
        s.IsReportTask = raw.Bool("report_task") ?? false;
        s.IsBillingTask = raw.Bool("billing_task") ?? false;
        s.DeletedUtc = raw.Time("deleted");
    }

    protected override void Deactivate(Subtask s) => Db.Subtasks.Remove(s);
}

// Uptick's taskstocktransactions -> TaskStockTransaction: stock taken for (or returned from) a task
public class TaskStockTransactionMapper : UptickEntityMapper<TaskStockTransaction>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, int> _users = new();
    private Dictionary<long, int> _products = new();
    private Dictionary<long, JsonElement> _contentTypes = new();

    public TaskStockTransactionMapper(AppDbContext db) : base(db) { }

    public override string Resource => "taskstocktransactions";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = await TaskWorkLookups.TasksAsync(Db, ct);
        _users = await TaskWorkLookups.UsersAsync(Db, ct);
        _products = await TaskWorkLookups.ProductsAsync(Db, ct);
        _contentTypes = await TaskWorkLookups.RawAsync(Db, "contenttypes", ct);
    }

    protected override async Task<Dictionary<long, TaskStockTransaction>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return Match(await Db.TaskStockTransactions.Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value)).ToListAsync(ct), s => s.UptickId);
    }

    protected override TaskStockTransaction Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(TaskStockTransaction s, UptickRaw raw)
    {
        s.UptickId = raw.Id;
        s.ClientTaskId = TaskWorkLookups.Task(raw, _tasks);
        s.ProductId = raw.Rel("product") is { } p && _products.TryGetValue(p, out var product) ? product : null;
        s.Quantity = raw.Decimal("delta") ?? 0;
        s.CostPrice = raw.Decimal("cost_price");
        s.UptickUserId = raw.Rel("committed_by") is { } u && _users.TryGetValue(u, out var user) ? user : null;
        s.CreatedUtc = raw.Time("created");
        s.OtherType = raw.Rel("other_content_type") is { } c && _contentTypes.TryGetValue(c, out var type)
            ? RoutineServiceTypeMapper.Cap(UptickRaw.Str(type, "model"), 50) : null;
        s.OtherUptickId = s.OtherType == null ? null : UptickRaw.Long(raw.A, "other_object_id");
    }

    protected override void Deactivate(TaskStockTransaction s) => Db.TaskStockTransactions.Remove(s);
}
