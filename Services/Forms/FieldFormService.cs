using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace FyreApp.Services.Forms;

// A form template a technician can fill in: its latest version
public sealed record FormTemplateOption(string VersionId, string Name);

// A form on a task: filling one in (ResponseId null), or one submitted (read only)
public sealed class FieldFormVm
{
    public int TaskId { get; set; }
    public string? TaskRef { get; set; }
    public string Name { get; set; } = "Form";
    public string VersionId { get; set; } = "";
    public FormDefinition Definition { get; set; } = new();

    // Answers by field key: text, true/false, a list of choices, a signature data: URL
    public JsonObject Values { get; set; } = new();

    public int? ResponseId { get; set; }
    public string? Ref { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public string? SubmittedBy { get; set; }
    public bool IsComplete { get; set; }

    // Required fields left empty on a submit
    public IReadOnlyList<string> Missing { get; set; } = [];

    public bool ReadOnly => ResponseId != null;

    public string Text(string key) => Value(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    public bool Checked(string key) => Value(key) is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public bool Chosen(string key, string value) => Value(key) is JsonArray a && a.Any(x => x is JsonValue j && j.TryGetValue<string>(out var s) && s == value);

    // Uptick keeps answers under lower-cased keys ("pi-signature" for the template's "pI-signature")
    private JsonNode? Value(string key) =>
        Values[key] ?? Values.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}

// A task's submitted form, listed on the field page
public sealed record FieldFormSummary(int Id, string Name, string? Ref, DateTime? SubmittedUtc, string? By, bool IsComplete);

public sealed record FormSaved(FieldResult Result, int? ResponseId = null, IReadOnlyList<string>? Missing = null)
{
    // What was entered, to show again when it can't be saved
    public JsonObject? Values { get; init; }
}

public interface IFieldFormService
{
    Task<IReadOnlyList<FormTemplateOption>> TemplatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FieldFormSummary>> TaskFormsAsync(int taskId, CancellationToken ct = default);

    // A blank form for the task, filled with what the template takes from the task (its property, client, …)
    Task<FieldFormVm?> NewAsync(int taskId, string versionId, int? technicianId, CancellationToken ct = default);

    Task<FormSaved> SubmitAsync(int taskId, int technicianId, string versionId, IEnumerable<KeyValuePair<string, StringValues>> form, CancellationToken ct = default);

    // A submitted form, from FyreApp or migrated from Uptick
    Task<FieldFormVm?> ViewAsync(int taskId, int responseId, CancellationToken ct = default);
}

// Forms in the field app: Uptick's form templates (dynamicformtemplates and their versions, held in the raw copy),
// filled in on a task and saved as DynamicFormResponse with their answers in Values
public partial class FieldFormService : IFieldFormService
{
    private const string Templates = "dynamicformtemplates", Versions = "dynamicformtemplateversions";
    private const int MaxSignatureLength = 1_500_000;

    private readonly AppDbContext _db;
    public FieldFormService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<FormTemplateOption>> TemplatesAsync(CancellationToken ct = default)
    {
        var names = (await _db.UptickRecords.AsNoTracking()
                .Where(r => r.Resource == Templates && r.DeletedUtc == null)
                .Select(r => new { r.UptickId, r.Attributes })
                .ToListAsync(ct))
            .ToDictionary(r => r.UptickId, r => Name(r.Attributes) ?? "Form");
        var versions = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == Versions && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Relationships })
            .ToListAsync(ct);

        // Each template's latest version
        return versions
            .Select(v => (Version: v.UptickId, Template: RelId(v.Relationships, "template")))
            .Where(v => v.Template != null && names.ContainsKey(v.Template))
            .GroupBy(v => v.Template!)
            .Select(g => new FormTemplateOption(g.MaxBy(v => long.TryParse(v.Version, out var n) ? n : 0).Version, names[g.Key]))
            .OrderBy(t => t.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<FieldFormSummary>> TaskFormsAsync(int taskId, CancellationToken ct = default) =>
        (await _db.DynamicFormResponses.AsNoTracking()
            .Where(f => f.ClientTaskId == taskId)
            .OrderBy(f => f.SubmittedUtc)
            .Select(f => new { f.Id, f.TemplateName, f.Ref, f.SubmittedUtc, By = f.UptickUser != null ? f.UptickUser.Name : null, f.IsComplete })
            .ToListAsync(ct))
        .Select(f => new FieldFormSummary(f.Id, f.TemplateName ?? "Form", f.Ref, f.SubmittedUtc, f.By, f.IsComplete))
        .ToList();

    public async Task<FieldFormVm?> NewAsync(int taskId, string versionId, int? technicianId, CancellationToken ct = default)
    {
        var task = await TaskAsync(taskId, ct);
        var version = await VersionAsync(versionId, ct);
        if (task == null || version == null) return null;
        var technician = technicianId == null ? null : await _db.UptickUsers.Where(u => u.Id == technicianId).Select(u => u.Name).FirstOrDefaultAsync(ct);

        var values = new JsonObject();
        foreach (var f in version.Value.Definition.Fields.Where(f => f.Initial != null))
            values[f.Key] = Initial(f.Initial!, task, technician);
        return new FieldFormVm
        {
            TaskId = task.Id, TaskRef = task.DisplayRef, Name = version.Value.Name, VersionId = versionId,
            Definition = version.Value.Definition, Values = values
        };
    }

    public async Task<FormSaved> SubmitAsync(int taskId, int technicianId, string versionId,
        IEnumerable<KeyValuePair<string, StringValues>> form, CancellationToken ct = default)
    {
        var task = await TaskAsync(taskId, ct);
        if (task == null) return new(new(FieldOutcome.NotFound));
        if (!FieldService.IsOpen(task)) return new(new(FieldOutcome.Invalid, "This task is closed."));
        var version = await VersionAsync(versionId, ct);
        if (version == null) return new(new(FieldOutcome.NotFound, "That form isn't there any more."));

        var posted = form.ToDictionary(p => p.Key, p => p.Value);
        string One(string key) => posted.TryGetValue(key, out var v) ? (v.FirstOrDefault() ?? "").Trim() : "";

        var values = new JsonObject();
        var missing = new List<string>();
        foreach (var f in version.Value.Definition.Fields.Where(f => f.TakesValue))
        {
            JsonNode? value;
            bool empty;
            switch (f.Type)
            {
                case FormField.Checkbox:
                    var on = posted.TryGetValue(f.Key, out var c) && c.Contains("true");
                    (value, empty) = (on, !on);
                    break;
                case FormField.CheckboxGroup:
                    var chosen = posted.TryGetValue(f.Key, out var g)
                        ? g.Where(x => f.Options.Any(o => o.Value == x)).Distinct().ToList() : [];
                    (value, empty) = (new JsonArray(chosen.Select(x => (JsonNode?)x).ToArray()), chosen.Count == 0);
                    break;
                case FormField.Assertion:
                    var answer = One(f.Key) is "pass" or "fail" or "na" ? One(f.Key) : "";
                    (value, empty) = (answer, answer == "");
                    if (answer == "fail") values[f.Key + "__comment"] = One(f.Key + "__comment");
                    break;
                case FormField.Signature:
                    var sig = One(f.Key);
                    if (!sig.StartsWith("data:image/png;base64,", StringComparison.Ordinal) || sig.Length > MaxSignatureLength) sig = "";
                    (value, empty) = (sig, sig == "");
                    break;
                default:
                    var text = One(f.Key);
                    (value, empty) = (text, text == "");
                    break;
            }
            values[f.Key] = value;
            if (f.Required && empty) missing.Add(f.Caption);
        }
        if (missing.Count > 0)
            return new(new(FieldOutcome.Invalid, "Fill in: " + string.Join(", ", missing) + "."), null, missing) { Values = values };

        var response = new DynamicFormResponse
        {
            ClientTaskId = task.Id,
            UptickUserId = technicianId,
            TemplateName = version.Value.Name.Length > 200 ? version.Value.Name[..200] : version.Value.Name,
            TemplateVersion = versionId,
            TargetContentType = DynamicFormResponse.TaskContentType,
            TargetObjectId = task.UptickId?.ToString(),
            SubmittedUtc = DateTime.UtcNow,
            IsComplete = true,
            Values = values.ToJsonString()
        };
        _db.DynamicFormResponses.Add(response);
        await _db.SaveChangesAsync(ct);
        return new(FieldResult.Done, response.Id);
    }

    public async Task<FieldFormVm?> ViewAsync(int taskId, int responseId, CancellationToken ct = default)
    {
        var response = await _db.DynamicFormResponses.AsNoTracking().Include(f => f.UptickUser).Include(f => f.ClientTask)
            .FirstOrDefaultAsync(f => f.Id == responseId && f.ClientTaskId == taskId, ct);
        if (response == null) return null;

        // A migrated response's answers are in its raw copy
        var json = response.Values;
        if (json == null && response.UptickId is { } uptickId)
        {
            var raw = await _db.UptickRecords.AsNoTracking()
                .Where(r => r.Resource == "dynamicformresponses" && r.UptickId == uptickId.ToString())
                .Select(r => r.Attributes).FirstOrDefaultAsync(ct);
            json = raw == null ? null : JsonNode.Parse(raw)?["values"]?.ToJsonString();
        }

        var version = response.TemplateVersion == null ? null : await VersionAsync(response.TemplateVersion, ct);
        return new FieldFormVm
        {
            TaskId = taskId,
            TaskRef = response.ClientTask?.DisplayRef,
            Name = response.TemplateName ?? version?.Name ?? "Form",
            VersionId = response.TemplateVersion ?? "",
            Definition = version?.Definition ?? new FormDefinition(),
            Values = (json == null ? null : JsonNode.Parse(json) as JsonObject) ?? new JsonObject(),
            ResponseId = response.Id,
            Ref = response.Ref,
            SubmittedUtc = response.SubmittedUtc,
            SubmittedBy = response.UptickUser?.Name,
            IsComplete = response.IsComplete
        };
    }

    private Task<ClientTask?> TaskAsync(int taskId, CancellationToken ct) =>
        _db.ClientTasks.AsNoTracking()
            .Include(t => t.Property).ThenInclude(p => p.Client)
            .Include(t => t.Client)
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

    private async Task<(string Name, FormDefinition Definition)?> VersionAsync(string versionId, CancellationToken ct)
    {
        var version = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == Versions && r.UptickId == versionId)
            .Select(r => new { r.Attributes, r.Relationships })
            .FirstOrDefaultAsync(ct);
        if (version == null) return null;
        var templateId = RelId(version.Relationships, "template");
        var template = templateId == null ? null : await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == Templates && r.UptickId == templateId)
            .Select(r => r.Attributes).FirstOrDefaultAsync(ct);
        var config = JsonNode.Parse(version.Attributes)?["config"]?.GetValue<string>();
        return ((template == null ? null : Name(template)) ?? "Form", FormDefinition.Parse(config));
    }

    // What a template's "initial" fills in: Uptick's placeholders ("{{task.property.name}}", "{now_date}") from the task
    public static string Initial(string initial, ClientTask task, string? technician)
    {
        var now = DateTime.UtcNow.ToSydney();
        return Placeholder().Replace(initial, m => (m.Groups["double"].Success ? m.Groups["double"].Value : m.Groups["single"].Value) switch
        {
            "task.property.name" => task.Property?.Name ?? "",
            "task.property.client.name" or "task.client.name" => task.Client?.Name ?? task.Property?.Client?.Name ?? "",
            "task.property.address.display" or "task.property.address.address" => task.Property?.AddressDisplay ?? "",
            "task.name" => task.Title,
            "task.pk" => task.UptickId?.ToString() ?? task.Id.ToString(CultureInfo.InvariantCulture),
            "task.ref" => task.DisplayRef ?? "",
            "task.assigned_to" => task.AssignedTo?.Name ?? technician ?? "",
            "user_name" => technician ?? "",
            "now_date" => now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            "now_time" => now.ToString("h:mm tt", CultureInfo.InvariantCulture),
            _ => ""   // what FyreApp doesn't hold (client contacts, extra fields) is left to fill in
        });
    }

    [GeneratedRegex(@"\{\{\s*(?<double>[a-z_.]+)\s*\}\}|\{(?<single>[a-z_]+)\}")]
    private static partial Regex Placeholder();

    private static string? Name(string attributes) => JsonNode.Parse(attributes)?["name"]?.GetValue<string>();

    private static string? RelId(string? relationships, string name)
    {
        if (relationships == null) return null;
        using var doc = JsonDocument.Parse(relationships);
        return doc.RootElement.TryGetProperty(name, out var rel) && rel.TryGetProperty("data", out var data)
               && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var id)
            ? id.ToString() : null;
    }
}

// Views/Field/_FormNode.cshtml and _FormField.cshtml: one piece of the form, with the form it's in
public sealed record FormNodePartial(FieldFormVm Form, FormLayoutNode Node);
public sealed record FormFieldPartial(FieldFormVm Form, FormField Field);
