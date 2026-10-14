using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// An export whose rows are refreshed from each export, matched on Uptick ID: new rows are added, existing ones
// updated (like reference data), none deleted. Subclasses say how a row maps onto a record.
public abstract class RecordImporter<T> : UptickImporter where T : class, IUptickRecord, new()
{
    protected RecordImporter(AppDbContext db) : base(db) { }

    // The ID column ("ID", or "id" in Uptick's lower-case exports)
    protected virtual string IdHeader => "ID";

    // e.g. "invoices", for the import's notes
    protected abstract string Noun { get; }

    protected abstract void Map(T record, ImportRow row);

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.Set<T>().Where(r => r.ExternalId != null).ToListAsync(ct))
            .ToDictionary(r => r.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get(IdHeader);
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var record))
            {
                record = new T { ExternalId = id };
                byExt[id!] = record;
                if (!dryRun) Db.Set<T>().Add(record);
                created++;
            }
            else updated++;

            if (dryRun) continue;
            Map(record, row);
            record.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, Noun);
        if (rows.Count == 0)
            ctx.Result.Notes.Add($"The export has no {Noun} (Uptick exported the column headers only).");
        if (!dryRun) await Db.SaveChangesAsync(ct);
    }

    protected static string Req(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}

// An export with no ID column: each import replaces what was there (a snapshot)
public abstract class SnapshotImporter<T> : UptickImporter where T : class
{
    protected SnapshotImporter(AppDbContext db) : base(db) { }

    protected abstract string Noun { get; }

    // The record for a row, or null to skip it (with an issue)
    protected abstract T? Build(ImportRow row, ImportContext ctx);

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var records = rows.Select(r => Build(r, ctx)).OfType<T>().ToList();
        var replaced = await Db.Set<T>().CountAsync(ct);

        ctx.Result.Created = records.Count;
        ctx.Result.SkippedExisting = 0;
        if (replaced > 0)
            ctx.Result.Notes.Add($"The {replaced} {Noun} already in FyreApp {(dryRun ? "will be" : "were")} replaced by this export (it has no IDs to match on).");
        if (rows.Count == 0)
            ctx.Result.Notes.Add($"The export has no {Noun} (Uptick exported the column headers only).");
        if (dryRun) return;

        Db.Set<T>().RemoveRange(await Db.Set<T>().ToListAsync(ct));
        Db.Set<T>().AddRange(records);
        await Db.SaveChangesAsync(ct);
    }
}
