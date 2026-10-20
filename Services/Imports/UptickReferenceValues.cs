using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports;

public sealed record ExtraFieldValue(string Label, string Value, string? HelpText);

// Reading imported records with Uptick's reference data: their extra fields, and users' security groups
public static class UptickReferenceValues
{
    // A record's extra field values, in Uptick's order, blank ones left out: from its Uptick record's
    // "extra_fields" (by name) when it came through the API, else from the columns of its export row
    public static async Task<IReadOnlyList<ExtraFieldValue>> ExtraFieldsAsync(AppDbContext db, string contentType, string resource,
        long? uptickId, string? uptickData, CancellationToken ct = default)
    {
        var raw = uptickId == null ? null : await db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.UptickId == uptickId.Value.ToString())
            .Select(r => r.Attributes)
            .FirstOrDefaultAsync(ct);
        if (raw == null && string.IsNullOrWhiteSpace(uptickData)) return [];
        var fields = await db.ExtraFields.AsNoTracking()
            .Where(f => f.ContentType == contentType)
            .OrderBy(f => f.Order).ThenBy(f => f.Label)
            .ToListAsync(ct);
        return raw != null ? ApiExtraFields(fields, raw) : ExtraFields(fields, uptickData);
    }

    // From a record's API attributes: "extra_fields": {"ase_connected": "Yes", ...}
    public static IReadOnlyList<ExtraFieldValue> ApiExtraFields(IEnumerable<ExtraField> fields, string attributes)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(attributes);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            && doc.RootElement.TryGetProperty("extra_fields", out var extra) && extra.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var p in extra.EnumerateObject())
                values.TryAdd(p.Name, p.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => p.Value.GetString()!,
                    System.Text.Json.JsonValueKind.True => "Yes",
                    System.Text.Json.JsonValueKind.False => "No",
                    System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => "",
                    _ => p.Value.GetRawText()
                });

        return fields
            .Select(f => f.Name != null && values.TryGetValue(f.Name, out var v) && !string.IsNullOrWhiteSpace(v)
                ? new ExtraFieldValue(f.Label, ChoiceLabel(f, v), string.IsNullOrWhiteSpace(f.HelpText) ? null : f.HelpText) : null)
            .OfType<ExtraFieldValue>()
            .ToList();
    }

    // A choice field stores its choice's value ("gold_coast_city_council"); shown as its label
    private static string ChoiceLabel(ExtraField f, string value)
    {
        if (f.FieldSettings == null || !f.FieldSettings.Contains("choices")) return value;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(f.FieldSettings);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var c in choices.EnumerateArray())
                    if (c.ValueKind == System.Text.Json.JsonValueKind.Object
                        && c.TryGetProperty("value", out var cv) && cv.ToString() == value
                        && c.TryGetProperty("label", out var label) && label.ValueKind == System.Text.Json.JsonValueKind.String)
                        return label.GetString()!;
        }
        catch (System.Text.Json.JsonException) { }
        return value;
    }

    public static IReadOnlyList<ExtraFieldValue> ExtraFields(IEnumerable<ExtraField> fields, string? uptickData)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, value) in UptickJson.Parse(uptickData)) values.TryAdd(header, value);

        return fields
            .Select(f => values.TryGetValue(f.Label, out var v) && !string.IsNullOrWhiteSpace(v) ? new ExtraFieldValue(f.Label, v, f.HelpText) : null)
            .OfType<ExtraFieldValue>()
            .ToList();
    }

    // Uptick's users export joins a user's groups with " - ", and group names can contain " - " too
    // ("Technician - Technician - Higher Permissions" is "Technician" and "Technician - Higher Permissions").
    // Splits on the separators, taking the longest known group name at each point; unknown parts stay as they are.
    public static IReadOnlyList<string> SplitSecurityGroups(string? joined, IReadOnlyCollection<string> groupNames)
    {
        if (string.IsNullOrWhiteSpace(joined)) return [];
        var parts = joined.Split(" - ", StringSplitOptions.TrimEntries);
        var known = new HashSet<string>(groupNames, StringComparer.OrdinalIgnoreCase);
        var groups = new List<string>();

        for (var i = 0; i < parts.Length;)
        {
            var take = 1;
            for (var n = parts.Length - i; n > 1; n--)
            {
                if (known.Contains(string.Join(" - ", parts[i..(i + n)]))) { take = n; break; }
            }
            groups.Add(string.Join(" - ", parts[i..(i + take)]));
            i += take;
        }
        return groups;
    }
}
