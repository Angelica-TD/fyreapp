using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "extra fields" export -> ExtraField (reference data, refreshed from each export): the custom fields
// set up on assets, properties and users. Their values come in with those records' exports.
public class ExtraFieldImporter : UptickImporter
{
    public ExtraFieldImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.ExtraFields;
    public override string DisplayName => "Extra fields";
    public override string[] SignatureHeaders => new[] { "content_type", "field_settings", "widget" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.ExtraFields.Where(f => f.ExternalId != null).ToListAsync(ct))
            .ToDictionary(f => f.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("id");
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var field))
            {
                field = new ExtraField { ExternalId = id };
                byExt[id!] = field;
                if (!dryRun) Db.ExtraFields.Add(field);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            field.ContentType = Cap(row.Get("content_type"), 20);
            field.Order = row.GetInt("order") ?? 0;
            field.Label = Cap(row.Get("label"), 200) ?? row.Get("name") ?? $"Field {id}";
            field.Name = Cap(row.Get("name"), 200);
            field.Widget = Cap(row.Get("widget"), 50);
            field.Default = Cap(row.Get("default"), 500);
            field.IsRequired = row.GetBool("is_required") ?? false;
            field.HelpText = Cap(row.Get("help_text"), 1000);
            field.FieldSettings = row.Get("field_settings");
            field.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "extra fields");
        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
