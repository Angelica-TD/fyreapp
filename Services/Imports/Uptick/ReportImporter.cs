using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Reports" export -> ServiceReport, matched to Property on "Property Ref".
public class ReportImporter : UptickImporter
{
    public ReportImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Reports;
    public override string DisplayName => "Reports";
    public override string[] SignatureHeaders => new[] { "Report Type", "Property Ref", "Issued", "Inspected" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var sitesByRef = await LoadSiteIdsByRefAsync(ct);
        var taskIds = await LoadTaskIdsByExternalIdAsync(ct);
        var existing = ByExternalId(await Db.ServiceReports.AsNoTracking()
            .Where(r => r.ExternalId != null)
            .Select(r => new ImportedRecord(r.Id, r.ExternalId!, r.UptickData))
            .ToListAsync(ct));

        await AddPlaceholderSitesAsync(rows.Where(r => NewOrChanged(r, "ID", existing)), sitesByRef, "Property", dryRun, ctx, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<ServiceReport>();
        var toUpdate = new Dictionary<int, Action<ServiceReport>>();

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            var action = Claim(id, existing, seen, row, ctx, out var existingId);
            if (action == RowAction.Skip) continue;

            var propertyRef = row.Get("Property Ref");
            if (propertyRef == null || !sitesByRef.TryGetValue(propertyRef, out var siteId))
            {
                ctx.Skip("Missing property ref", id!, row.RowNumber, "Report has no Property Ref. Row skipped.");
                continue;
            }

            if (action == RowAction.Create)
            {
                var report = new ServiceReport { ExternalId = id };
                Map(report, row, siteId, taskIds);
                toCreate.Add(report);
            }
            else
            {
                toUpdate[existingId] = r => Map(r, row, siteId, taskIds);
            }
        }

        ctx.Result.Created = toCreate.Count;
        ctx.Result.Updated = toUpdate.Count;
        NoteNotInExport(ctx, existing, rows, "ID", "reports");
        if (dryRun) return;

        await ApplyUpdatesAsync(Db.ServiceReports, toUpdate, r => r.Id, ct);
        Db.ServiceReports.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // Everything a report takes from its Uptick row, for new and updated reports alike
    private static void Map(ServiceReport r, ImportRow row, int siteId, IReadOnlyDictionary<string, int> taskIds)
    {
        r.UptickData = row.ToJson();
        r.PropertyId = siteId;
        r.Ref = row.Get("Ref");
        r.ReportType = row.Get("Report Type");
        r.IssuedDate = row.GetDate("Issued");
        r.InspectedDate = row.GetDate("Inspected");
        r.Compliant = row.GetBool("Compliant");
        r.TaskRef = row.Get("Task Ref");
        r.TaskName = row.Get("Task");
        r.TaskExternalId = row.Get("Task Id");
        // Linked now when the task is already imported; otherwise the tasks import links it
        r.ClientTaskId = r.TaskExternalId != null && taskIds.TryGetValue(r.TaskExternalId, out var taskId) ? taskId : null;
        r.Technician = row.Get("Technician");
        r.Author = row.Get("Author");
        r.Published = row.GetBool("Published") ?? false;
        r.Amendment = row.GetBool("Amendment") ?? false;
        r.GeneralNote = row.Get("General Note");
        r.CriticalNote = row.Get("Critical Note");
    }
}
