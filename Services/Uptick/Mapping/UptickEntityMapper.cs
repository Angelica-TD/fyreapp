using System.Globalization;
using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// An endpoint mapped onto one FyreApp entity: each record upserts the row with its Uptick id, and one deleted in
// Uptick deactivates its row rather than removing it, so what points at it survives.
public abstract class UptickEntityMapper<T> : IUptickMapper where T : class
{
    protected UptickEntityMapper(AppDbContext db) => Db = db;

    protected AppDbContext Db { get; }

    public abstract string Resource { get; }

    public virtual Task BeginAsync(CancellationToken ct) => Task.CompletedTask;

    // The FyreApp rows the batch's records already have, by Uptick id
    protected abstract Task<Dictionary<long, T>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct);

    protected abstract T Create(UptickRaw raw);

    // Copies the record onto the row. Throws UptickMapException before changing anything when it can't
    // (e.g. a relationship FyreApp doesn't hold).
    protected abstract void Map(T entity, UptickRaw raw);

    protected abstract void Deactivate(T entity);

    // After the batch is saved
    protected virtual Task AfterAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<IReadOnlyDictionary<string, string>> MapAsync(IReadOnlyList<UptickRecord> batch, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>();
        var raws = new List<UptickRaw>();
        foreach (var record in batch)
        {
            if (!long.TryParse(record.UptickId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                failures[record.UptickId] = "The id isn't a number.";
                continue;
            }
            try
            {
                raws.Add(UptickRaw.Parse(record, id));
            }
            catch (JsonException ex)
            {
                failures[record.UptickId] = ex.Message;
            }
        }

        var existing = await ExistingAsync(raws, ct);
        var edits = await OpenEditsAsync(existing.Values, ct);
        foreach (var raw in raws)
        {
            existing.TryGetValue(raw.Id, out var entity);
            if (raw.Deleted)
            {
                if (entity != null) Deactivate(entity);
                continue;
            }

            try
            {
                if (entity == null)
                {
                    entity = Create(raw);
                    Map(entity, raw);
                    Db.Add(entity);
                    existing[raw.Id] = entity;
                }
                else
                {
                    Map(entity, raw);
                    if (edits.TryGetValue(IdOf(entity), out var open)) KeepLocalEdits(entity, raw, open);
                }
            }
            catch (Exception ex) when (ex is UptickMapException or InvalidOperationException or FormatException)
            {
                failures[raw.Record.UptickId] = ex.Message;
            }
        }

        // What mapping writes is Uptick's, not an edit made in FyreApp
        Db.RecordLocalEdits = false;
        try
        {
            await Db.SaveChangesAsync(ct);
            await AfterAsync(ct);
        }
        finally
        {
            Db.RecordLocalEdits = true;
        }
        return failures;
    }

    // Fields changed in FyreApp (§7): while Uptick's value is still the one it had at the edit, FyreApp's stays;
    // once Uptick's has changed, it wins and the edit is closed
    private void KeepLocalEdits(T entity, UptickRaw raw, List<LocalEdit> edits)
    {
        var entry = Db.Entry(entity);
        foreach (var edit in edits)
        {
            string? uptick;
            if (edit.Field == LocalEditLog.AssetTypes) uptick = UptickValue(entity, edit);
            else if (entry.Metadata.FindProperty(edit.Field) == null) continue;
            else uptick = LocalEditLog.Text(entry.Property(edit.Field).CurrentValue);

            if (uptick == edit.UptickValue) Keep(entity, edit);
            else LocalEditLog.Close(edit, "Uptick", "Uptick", DateTime.UtcNow);
        }
    }

    // A field's value as just mapped from Uptick, for a field that isn't a plain property (an asset's type)
    protected virtual string? UptickValue(T entity, LocalEdit edit) => edit.UptickValue;

    // Puts FyreApp's value back over what was just mapped
    protected virtual void Keep(T entity, LocalEdit edit)
    {
        var p = Db.Entry(entity).Property(edit.Field);
        p.CurrentValue = p.OriginalValue;
    }

    private async Task<Dictionary<int, List<LocalEdit>>> OpenEditsAsync(IEnumerable<T> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => Db.Entry(r).State != EntityState.Added).Select(IdOf).ToList();
        if (ids.Count == 0) return new Dictionary<int, List<LocalEdit>>();
        var entity = typeof(T).Name;
        return (await Db.LocalEdits.Where(e => e.ClosedUtc == null && e.Entity == entity && ids.Contains(e.RecordId)).ToListAsync(ct))
            .GroupBy(e => e.RecordId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private int IdOf(T entity) => (int)Db.Entry(entity).Property("Id").CurrentValue!;

    // The rows by Uptick id
    protected static Dictionary<long, T> Match(IReadOnlyList<T> rows, Func<T, long?> uptickId)
    {
        var map = new Dictionary<long, T>();
        foreach (var row in rows)
            if (uptickId(row) is { } id) map.TryAdd(id, row);
        return map;
    }
}
