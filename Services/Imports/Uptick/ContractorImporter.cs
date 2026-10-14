using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Contractors" export -> Contractor. Refreshed from each export, like reference data.
public class ContractorImporter : UptickImporter
{
    public ContractorImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Contractors;
    public override string DisplayName => "Contractors";
    public override string[] SignatureHeaders => new[] { "Primary Contact", "Phone BH", "Service Areas" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.Contractors.Where(c => c.ExternalId != null).ToListAsync(ct))
            .ToDictionary(c => c.ExternalId!, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var contractor))
            {
                contractor = new Contractor { ExternalId = id };
                byExt[id!] = contractor;
                if (!dryRun) Db.Contractors.Add(contractor);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            contractor.Name = Cap(row.Get("Name"), 200) ?? $"Contractor {id}";
            contractor.PrimaryContact = Cap(row.Get("Primary Contact"), 200);
            contractor.Email = Cap(row.Get("Email"), 200);
            contractor.EmailCc = Cap(row.Get("Email CC"), 1000);
            contractor.Phone = Cap(row.Get("Phone BH"), 100);
            contractor.Mobile = Cap(row.Get("Mobile"), 100);
            contractor.Category = Cap(row.Get("Category"), 100);
            contractor.Tags = Cap(row.Get("Tags"), 500);
            contractor.Address = Cap(row.Get("Address"), 500);
            contractor.Skills = Cap(row.Get("Skills"), 1000);
            contractor.ServiceAreas = Cap(row.Get("Service Areas"), 1000);
            contractor.Starred = row.GetBool("Starred") ?? false;
            contractor.Active = row.GetBool("Active") ?? true;
            contractor.CreatedUtc = row.GetTimestampUtc("Created");
            contractor.UpdatedUtc = row.GetTimestampUtc("Updated");
            contractor.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "contractors");

        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
