using System.Globalization;
using FyreApp.Models;

namespace FyreApp.Services.Timeline;

// Timeline events for edits made in FyreApp, worded as Uptick words them: "Location was changed to ...". Each record
// type lists the fields it logs, under Uptick's labels; comparing a snapshot before and after an edit gives the changes.
public static class TimelineLog
{
    public static IReadOnlyList<(string Field, string? Value)> AssetFields(Asset a) => new (string, string?)[]
    {
        ("Type", string.Join(", ", a.AssetTypes.Select(t => t.Name).OrderBy(n => n))),
        ("Label", a.Name),
        ("Location", a.Location),
        ("Variant", a.Variant),
        ("Internal reference", a.Ref),
        ("Serial", a.Serial),
        ("Barcode", a.Barcode),
        ("Make", a.Make),
        ("Model", a.Model),
        ("Size", a.Size),
        ("Quantity", a.Quantity),
        ("Compliant", CompliantWord(a.Compliance)),
        ("Active", a.IsActive ? "yes" : "no"),
        ("Base date", Date(a.BaseDate)),
        ("Installation date", Date(a.InstallationDate)),
        ("Last service date", Date(a.LastServiceDate)),
        ("Inspection order", a.InspectionOrder?.ToString(CultureInfo.InvariantCulture)),
        ("Internal note", a.InternalNote)
    };

    // Uptick's "Compliant" is yes / no; FyreApp keeps the export's Pass / Fail / N/A
    private static string? CompliantWord(string? compliance) => compliance?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "pass" or "true" or "yes" => "yes",
        "fail" or "false" or "no" => "no",
        _ => compliance
    };

    // Uptick shows dates in changes as dd/MM/yyyy
    private static string? Date(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    // One entry per field whose value differs (blank and missing count as the same)
    public static List<TimelineEvent> Changes(IReadOnlyList<(string Field, string? Value)> before,
        IReadOnlyList<(string Field, string? Value)> after, string? author, DateTime whenUtc)
    {
        static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().ReplaceLineEndings("\n");

        var was = before.ToDictionary(f => f.Field, f => Norm(f.Value));
        return after
            .Where(f => !string.Equals(was.GetValueOrDefault(f.Field), Norm(f.Value), StringComparison.Ordinal))
            .Select(f => new TimelineEvent { Field = f.Field, Value = Norm(f.Value), Author = author, WhenUtc = whenUtc })
            .ToList();
    }
}
