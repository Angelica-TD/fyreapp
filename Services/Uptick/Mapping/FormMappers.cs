using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's extrafields -> ExtraField: the custom fields set up on assets, properties and users, with the record
// type they're on. A field the extra fields export placed (ExternalId = its id) is matched and updated.
public class ExtraFieldMapper : UptickEntityMapper<ExtraField>
{
    public ExtraFieldMapper(AppDbContext db) : base(db) { }

    public override string Resource => "extrafields";

    protected override async Task<Dictionary<long, ExtraField>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.ExtraFields
            .Where(f => (f.UptickId != null && ids.Contains(f.UptickId.Value)) || (f.ExternalId != null && texts.Contains(f.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), f => f.UptickId, f => f.ExternalId);
    }

    protected override ExtraField Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(ExtraField f, UptickRaw raw)
    {
        f.UptickId = raw.Id;
        f.ExternalId = raw.Id.ToString();
        f.ContentType = raw.Rel("content_type")?.ToString();
        f.Order = raw.Int("order") ?? 0;
        f.Name = raw.Text("name", 200);
        f.Label = raw.Text("label", 200) ?? f.Name ?? $"Field {raw.Id}";
        f.Widget = raw.Text("widget", 50);
        f.Default = raw.Text("default", 500);
        f.IsRequired = raw.Bool("is_required") ?? false;
        f.HelpText = raw.Text("help_text", 1000);
        f.FieldSettings = UptickRaw.Get(raw.A, "field_settings") is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } s
            ? s.GetRawText() : raw.Text("field_settings");
    }

    // A field gone from Uptick isn't shown on its records any more
    protected override void Deactivate(ExtraField f) => Db.ExtraFields.Remove(f);
}

// Uptick's promptquestions -> PromptQuestion, with its promptset's name, ref and type. A question the prompt
// questions export placed (ExternalId = its id) is matched and updated.
public class PromptQuestionMapper : UptickEntityMapper<PromptQuestion>
{
    private Dictionary<long, (string? Name, string? Ref, string? Type)> _promptsets = new();

    public PromptQuestionMapper(AppDbContext db) : base(db) { }

    public override string Resource => "promptquestions";

    public override async Task BeginAsync(CancellationToken ct) =>
        _promptsets = (await RemarkMapper.RawAsync(Db, "promptsets", ct))
            .ToDictionary(s => s.Id, s => (UptickRaw.Str(s.A, "name"), UptickRaw.Str(s.A, "ref"), UptickRaw.Str(s.A, "type")));

    protected override async Task<Dictionary<long, PromptQuestion>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.PromptQuestions
            .Where(q => (q.UptickId != null && ids.Contains(q.UptickId.Value)) || (q.ExternalId != null && texts.Contains(q.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), q => q.UptickId, q => q.ExternalId);
    }

    protected override PromptQuestion Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(PromptQuestion q, UptickRaw raw)
    {
        (string? Name, string? Ref, string? Type)? set = raw.Rel("promptset") is { } s && _promptsets.TryGetValue(s, out var found) ? found : null;

        q.UptickId = raw.Id;
        q.ExternalId = raw.Id.ToString();
        q.PromptsetName = RoutineServiceTypeMapper.Cap(set?.Name, 200);
        q.PromptsetRef = RoutineServiceTypeMapper.Cap(set?.Ref, 100);
        q.PromptsetType = RoutineServiceTypeMapper.Cap(set?.Type, 100);
        q.Type = raw.Text("type", 100);
        q.Label = raw.Text("label", 500) ?? $"Question {raw.Id}";
        q.HelpText = raw.Text("help_text");
        q.Ref = raw.Text("ref", 100);
        q.Active = raw.Bool("is_active") ?? raw.Bool("active") ?? true;
        q.Required = raw.Bool("is_required") ?? raw.Bool("required") ?? false;
        q.Order = raw.Int("order");
    }

    protected override void Deactivate(PromptQuestion q) => q.Active = false;
}

// Uptick's propertyschedules -> PropertySchedule, at its property (e.g. its occupancy permit). These replace the
// schedules export's snapshot, whose rows (no ids) are cleared on the first pass.
public class PropertyScheduleMapper : UptickEntityMapper<PropertySchedule>
{
    private Dictionary<long, (int Id, string? Ref, string Name, string? Client)> _properties = new();

    public PropertyScheduleMapper(AppDbContext db) : base(db) { }

    public override string Resource => "propertyschedules";

    public override async Task BeginAsync(CancellationToken ct)
    {
        var snapshot = await Db.PropertySchedules.Where(s => s.UptickId == null).ToListAsync(ct);
        if (snapshot.Count > 0)
        {
            Db.PropertySchedules.RemoveRange(snapshot);
            await Db.SaveChangesAsync(ct);
        }
        _properties = await QuoteProperties.LoadAsync(Db, ct);
    }

    protected override async Task<Dictionary<long, PropertySchedule>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return await Db.PropertySchedules
            .Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value))
            .ToDictionaryAsync(s => s.UptickId!.Value, ct);
    }

    protected override PropertySchedule Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(PropertySchedule s, UptickRaw raw)
    {
        if (raw.Rel("property") is not { } propertyId) throw new UptickMapException("It has no property.");
        if (!_properties.TryGetValue(propertyId, out var property)) throw new UptickMapException($"Its property ({propertyId}) isn't in FyreApp.");

        s.UptickId = raw.Id;
        s.PropertyId = property.Id;
        s.PropertyExternalId = propertyId.ToString();
        s.PropertyRef = RoutineServiceTypeMapper.Cap(property.Ref, 64);
        s.PropertyName = RoutineServiceTypeMapper.Cap(property.Name, 300);
        s.Number = raw.Text("number", 100);
        s.Date = raw.Date("date");
        s.Type = raw.Text("type_display", 100) ?? raw.Text("type", 100);
        s.Issuer = raw.Text("issuer", 300);
        s.Location = raw.Text("location", 300);
    }

    // A schedule gone from Uptick is no longer held
    protected override void Deactivate(PropertySchedule s) => Db.PropertySchedules.Remove(s);
}

// Uptick's dynamicformresponses -> DynamicFormResponse, on the task it was submitted on, by its submitter, with
// its form's name (template version -> template). A response the form responses export placed (ExternalId = its
// id) is matched and updated.
public class DynamicFormResponseMapper : UptickEntityMapper<DynamicFormResponse>
{
    private Dictionary<long, int> _tasks = new();
    private Dictionary<long, int> _users = new();
    private Dictionary<long, string> _templates = new();

    public DynamicFormResponseMapper(AppDbContext db) : base(db) { }

    public override string Resource => "dynamicformresponses";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _tasks = (await Db.ClientTasks.AsNoTracking().Where(t => t.UptickId != null)
                .Select(t => new { UptickId = t.UptickId!.Value, t.Id }).ToListAsync(ct))
            .ToDictionary(t => t.UptickId, t => t.Id);
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Id }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => u.Id);

        var names = await RoutineServiceTypeMapper.RawNamesAsync(Db, "dynamicformtemplates", ct);
        _templates = new Dictionary<long, string>();
        foreach (var version in await RemarkMapper.RawAsync(Db, "dynamicformtemplateversions", ct))
            if (RemarkMapper.Rel(version.R, "template") is { } template && names.TryGetValue(template, out var name))
                _templates[version.Id] = name;
    }

    protected override async Task<Dictionary<long, DynamicFormResponse>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.DynamicFormResponses
            .Where(f => (f.UptickId != null && ids.Contains(f.UptickId.Value)) || (f.ExternalId != null && texts.Contains(f.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), f => f.UptickId, f => f.ExternalId);
    }

    protected override DynamicFormResponse Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(DynamicFormResponse f, UptickRaw raw)
    {
        var contentType = raw.Rel("target_contenttype")?.ToString();
        var target = raw.Text("target_object_id", 64) ?? UptickRaw.Long(raw.A, "target_object_id")?.ToString();
        int? task = null;
        if (contentType == DynamicFormResponse.TaskContentType && long.TryParse(target, out var taskId))
            task = _tasks.TryGetValue(taskId, out var t) ? t : throw new UptickMapException($"Its task ({taskId}) isn't in FyreApp.");
        int? submitter = null;
        if (raw.Rel("submitter") is { } userId)
            submitter = _users.TryGetValue(userId, out var u) ? u : throw new UptickMapException($"Its submitter ({userId}) isn't in FyreApp.");

        f.UptickId = raw.Id;
        f.ExternalId = raw.Id.ToString();
        f.Ref = raw.Text("ref", 30);
        f.SubmittedUtc = raw.Time("submitted");
        f.ClientTaskId = task;
        f.UptickUserId = submitter;
        f.SubmitterExternalId = raw.Rel("submitter")?.ToString();
        f.TemplateVersion = raw.Rel("template_version")?.ToString();
        f.TemplateName = RoutineServiceTypeMapper.Cap(raw.Rel("template_version") is { } v ? _templates.GetValueOrDefault(v) : null, 200);
        f.TargetContentType = contentType;
        f.TargetObjectId = target;
        f.IsComplete = raw.Bool("is_complete") ?? false;
    }

    // A response gone from Uptick is gone from its task
    protected override void Deactivate(DynamicFormResponse f) => Db.DynamicFormResponses.Remove(f);
}
