using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Suppliers" export -> Supplier. Refreshed from each export, like reference data.
public class SupplierImporter : UptickImporter
{
    public SupplierImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Suppliers;
    public override string DisplayName => "Suppliers";
    public override string[] SignatureHeaders => new[] { "Business Number", "Accounts Contact Name", "Purchasing Email" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.Suppliers.Where(s => s.ExternalId != null).ToListAsync(ct))
            .ToDictionary(s => s.ExternalId!, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var supplier))
            {
                supplier = new Supplier { ExternalId = id };
                byExt[id!] = supplier;
                if (!dryRun) Db.Suppliers.Add(supplier);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            supplier.Name = Cap(row.Get("Name"), 200) ?? $"Supplier {id}";
            supplier.Ref = Cap(row.Get("Ref"), 50);
            supplier.BusinessNumber = Cap(row.Get("Business Number"), 50);
            supplier.PaymentTerms = Cap(row.Get("Partner Terms"), 100);
            supplier.PaymentDay = Cap(row.Get("Payment Day"), 50);
            supplier.AccountsContactName = Cap(row.Get("Accounts Contact Name"), 200);
            supplier.AccountsEmail = Cap(row.Get("Accounts Email"), 200);
            supplier.PurchasingContactName = Cap(row.Get("Purchasing Contact Name"), 200);
            supplier.PurchasingEmail = Cap(row.Get("Purchasing Email"), 200);
            supplier.AccountingOrganisation = Cap(row.Get("Accounting Organisation"), 200);
            supplier.Active = row.GetBool("Is Active") ?? true;
            supplier.CreatedUtc = row.GetTimestampUtc("Created");
            supplier.UpdatedUtc = row.GetTimestampUtc("Updated");
            supplier.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "suppliers");

        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
