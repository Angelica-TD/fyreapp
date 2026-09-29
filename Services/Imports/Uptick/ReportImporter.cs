using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Reports" export -> ServiceReport, matched to Site on "Property Ref".
public class ReportImporter : UptickImporter
{
    public ReportImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Reports;
    public override string DisplayName => "Reports";
    public override string[] SignatureHeaders => new[] { "Report Type", "Property Ref", "Issued", "Inspected" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var sitesByRef = await LoadSiteIdsByRefAsync(ct);
        var existing = (await Db.ServiceReports.AsNoTracking()
                .Where(r => r.ExternalId != null)
                .Select(r => r.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<ServiceReport>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, existing, seen, row, ctx)) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Property not found", propertyRef ?? "(blank)", row.RowNumber,
                    $"No property with ref '{propertyRef}' ({row.Get("Property")}). Import properties first. Row skipped.");
                continue;
            }

            toCreate.Add(new ServiceReport
            {
                ExternalId = id,
                SiteId = siteId,
                Ref = row.Get("Ref"),
                ReportType = row.Get("Report Type"),
                IssuedDate = row.GetDate("Issued"),
                InspectedDate = row.GetDate("Inspected"),
                Compliant = row.GetBool("Compliant"),
                TaskRef = row.Get("Task Ref"),
                TaskName = row.Get("Task"),
                Technician = row.Get("Technician"),
                Author = row.Get("Author"),
                Published = row.GetBool("Published") ?? false,
                Amendment = row.GetBool("Amendment") ?? false,
                GeneralNote = row.Get("General Note"),
                CriticalNote = row.Get("Critical Note")
            });
        }

        ctx.Result.Created = toCreate.Count;
        if (dryRun) return;

        Db.ServiceReports.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }
}
