using System.Globalization;
using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Infrastructure;

// "Edited in FyreApp": a migrated record's fields changed here and not (yet) changed in Uptick since
// (docs/uptick-migration.md §7), with an admin reset to Uptick's value
public partial class LocalEditsViewComponent : ViewComponent
{
    public record Row(LocalEdit Edit, string Field, string FyreApp, string Uptick);

    private readonly AppDbContext _db;

    public LocalEditsViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync(string entity, int id)
    {
        var edits = await _db.LocalEdits.AsNoTracking()
            .Where(e => e.ClosedUtc == null && e.Entity == entity && e.RecordId == id)
            .OrderBy(e => e.Field)
            .ToListAsync();

        // An asset's type is stored by id
        var typeIds = edits.Where(e => e.Field == LocalEditLog.AssetTypes)
            .SelectMany(e => new[] { e.FyreAppValue, e.UptickValue })
            .Select(v => int.TryParse(v, out var n) ? n : 0).Where(n => n > 0).ToList();
        var types = typeIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.AssetTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);

        string Show(LocalEdit e, string? value) =>
            value == null ? "-"
            : e.Field == LocalEditLog.AssetTypes ? (int.TryParse(value, out var t) && types.TryGetValue(t, out var name) ? name : value)
            : value.Contains('T') && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
                ? (d.TimeOfDay == TimeSpan.Zero ? d.ToString("d MMM yyyy") : d.ToLocalTime().ToString("d MMM yyyy h:mm tt"))
            : value switch { "true" => "Yes", "false" => "No", _ => value };

        return View(edits.Select(e => new Row(e, Label(e.Field), Show(e, e.FyreAppValue), Show(e, e.UptickValue))).ToList());
    }

    // "InspectionOrder" -> "Inspection order"; "AssetTypes" -> "Asset type"
    private static string Label(string field)
    {
        if (field == LocalEditLog.AssetTypes) return "Asset type";
        var words = Words().Replace(field, " ").ToLowerInvariant();
        return words.Length == 0 ? field : char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex Words();
}
