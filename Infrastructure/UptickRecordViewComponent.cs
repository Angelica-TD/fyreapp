using System.Globalization;
using System.Text.Json;
using FyreApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Infrastructure;

// "Uptick data": every field of a migrated record's raw copy from Uptick's API (docs/uptick-migration.md §3.2), so
// what FyreApp doesn't map is still to hand. Blank fields are left out; links show the id they point to.
public class UptickRecordViewComponent : ViewComponent
{
    public record Field(string Name, string Value);

    private readonly AppDbContext _db;

    public UptickRecordViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync(string resource, long? uptickId)
    {
        if (uptickId is not { } id) return Content("");
        var text = id.ToString(CultureInfo.InvariantCulture);
        var raw = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == resource && r.UptickId == text)
            .Select(r => new { r.Attributes, r.Relationships })
            .FirstOrDefaultAsync();
        if (raw == null) return Content("");
        return View(Fields(raw.Attributes, raw.Relationships));
    }

    public static IReadOnlyList<Field> Fields(string attributes, string relationships)
    {
        var fields = new List<Field>();
        using (var a = JsonDocument.Parse(attributes))
            if (a.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in a.RootElement.EnumerateObject())
                    if (Show(p.Value) is { } value) fields.Add(new Field(p.Name, value));
        using (var r = JsonDocument.Parse(relationships))
            if (r.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in r.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("data", out var d) && Link(d) is { } value)
                        fields.Add(new Field(p.Name, value));
        return fields;
    }

    private static string? Show(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Object => v.EnumerateObject().Any() ? v.GetRawText() : null,
        JsonValueKind.Array => v.GetArrayLength() > 0 ? v.GetRawText() : null,
        _ => null
    };

    // {"type": "Client", "id": "2806"} -> "Client 2806"; a to-many link lists each
    private static string? Link(JsonElement d) => d.ValueKind switch
    {
        JsonValueKind.Object => $"{d.GetProperty("type").GetString()} {d.GetProperty("id")}",
        JsonValueKind.Array when d.GetArrayLength() > 0 => string.Join(", ", d.EnumerateArray().Select(x => $"{x.GetProperty("type").GetString()} {x.GetProperty("id")}")),
        _ => null
    };
}
