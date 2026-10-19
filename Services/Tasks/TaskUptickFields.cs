using System.Globalization;
using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Uptick.Mapping;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Tasks;

// What only Uptick has about a task (notes, authorisation, service group, ...), keyed by the Tasks export's column
// headers that the task page reads. A task migrated through the API takes them from its raw copy, over any export
// row it was first imported with (which keeps the columns the API doesn't have, e.g. tags).
public static class TaskUptickFields
{
    public static async Task<Dictionary<string, string>> ForAsync(AppDbContext db, ClientTask task, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, value) in Imports.UptickJson.Parse(task.UptickData)) values.TryAdd(header, value);
        if (task.UptickId is not { } id) return values;

        var text = id.ToString(CultureInfo.InvariantCulture);
        var raw = await db.UptickRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Resource == "tasks" && r.UptickId == text, ct);
        if (raw == null) return values;

        using var a = JsonDocument.Parse(raw.Attributes);
        using var r = JsonDocument.Parse(raw.Relationships);
        var attributes = a.RootElement;
        var relationships = r.RootElement;

        void Set(string header, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) values[header] = value.Trim();
            else values.Remove(header);
        }
        string? Attr(string name) => UptickRaw.Get(attributes, name) switch
        {
            { ValueKind: JsonValueKind.String } v => v.GetString(),
            { ValueKind: JsonValueKind.Number } v => v.GetRawText(),
            _ => null
        };
        long? Rel(string name) =>
            UptickRaw.Get(relationships, name) is { ValueKind: JsonValueKind.Object } rel && rel.TryGetProperty("data", out var d)
            && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var relId) && long.TryParse(relId.ToString(), out var n) ? n : null;
        // A date-time as the date it was where it happened (its own offset)
        string? Day(string name) =>
            Attr(name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

        Set("Status", UptickRaw.Get(relationships, "status") is { ValueKind: JsonValueKind.Object } status
            && status.TryGetProperty("data", out var sd) && sd.ValueKind == JsonValueKind.Object && sd.TryGetProperty("id", out var sid)
                ? sid.GetString() : null);
        Set("Description", Attr("description"));
        Set("Scheduled Date", Attr("scheduled_date") ?? Day("scheduled_at"));
        Set("Performed Date", Day("status_changed_inspected"));
        Set("Technician Note", Attr("technician_note"));
        Set("Invoice Note", Attr("invoice_note"));
        Set("Internal Note", Attr("internal_note"));
        Set("Authorisation Ref", Attr("authorisation_ref"));
        Set("Authorisation Amount", Attr("authorisation_amount"));
        Set("Access Window", Attr("access_window"));
        Set("Est. Duration", Attr("estimated_duration"));
        Set("Invoiced", Attr("invoiced_date") != null ? "Yes" : null);

        Set("Service Group", Rel("servicegroup") is { } group ? await RawNameAsync(db, "servicegroups", group, "name", ct) : null);
        Set("Round", Rel("round") is { } round ? await RawNameAsync(db, "rounds", round, "name", ct) : null);
        Set("Billing Card Name", Rel("billingcard") is { } card ? await RawNameAsync(db, "billingcards", card, "name", ct) : null);
        Set("Salesperson", Rel("salesperson") is { } sales ? await UserNameAsync(db, sales, ct) : null);
        Set("Assigned Office", Rel("assigned_office") is { } office ? await UserNameAsync(db, office, ct) : null);
        Set("Parent Task", Rel("parent_task") is { } parent
            ? await db.ClientTasks.AsNoTracking().Where(t => t.UptickId == parent).Select(t => t.Ref).FirstOrDefaultAsync(ct)
            : null);
        return values;
    }

    private static async Task<string?> UserNameAsync(AppDbContext db, long id, CancellationToken ct) =>
        await db.UptickUsers.AsNoTracking().Where(u => u.UptickId == id).Select(u => u.Name).FirstOrDefaultAsync(ct);

    private static async Task<string?> RawNameAsync(AppDbContext db, string resource, long id, string attribute, CancellationToken ct)
    {
        var text = id.ToString(CultureInfo.InvariantCulture);
        var attributes = await db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.UptickId == text)
            .Select(r => r.Attributes)
            .FirstOrDefaultAsync(ct);
        if (attributes == null) return null;
        using var a = JsonDocument.Parse(attributes);
        return UptickRaw.Str(a.RootElement, attribute);
    }
}
