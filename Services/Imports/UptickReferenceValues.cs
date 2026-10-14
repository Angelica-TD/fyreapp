using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports;

public sealed record ExtraFieldValue(string Label, string Value, string? HelpText);

// Reading imported records with Uptick's reference data: their extra fields, and users' security groups
public static class UptickReferenceValues
{
    // A record's extra field values, from the columns of its Uptick row that are extra fields of its record
    // type, in Uptick's order. Blank ones are left out.
    public static async Task<IReadOnlyList<ExtraFieldValue>> ExtraFieldsAsync(AppDbContext db, string contentType, string? uptickData,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uptickData)) return [];
        var fields = await db.ExtraFields.AsNoTracking()
            .Where(f => f.ContentType == contentType)
            .OrderBy(f => f.Order).ThenBy(f => f.Label)
            .ToListAsync(ct);
        return ExtraFields(fields, uptickData);
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
