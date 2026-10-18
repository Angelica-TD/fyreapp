using System.Globalization;
using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// An endpoint mapped onto one FyreApp entity: each record upserts the row with its Uptick id (or, for rows a file
// import made, the ExternalId it gave them), and one deleted in Uptick deactivates its row rather than removing
// it, so what points at it survives.
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
                else Map(entity, raw);
            }
            catch (Exception ex) when (ex is UptickMapException or InvalidOperationException or FormatException)
            {
                failures[raw.Record.UptickId] = ex.Message;
            }
        }

        await Db.SaveChangesAsync(ct);
        await AfterAsync(ct);
        return failures;
    }

    // The rows by Uptick id: those that have it, then those a file import made, by the text it stored in
    // ExternalId (Uptick's id, or a property's ref)
    protected static Dictionary<long, T> Match(IReadOnlyList<T> rows, IEnumerable<(long Id, string? Text)> keys,
        Func<T, long?> uptickId, Func<T, string?> externalId)
    {
        var map = new Dictionary<long, T>();
        foreach (var row in rows)
            if (uptickId(row) is { } id) map.TryAdd(id, row);

        var byText = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, text) in keys)
            if (!string.IsNullOrWhiteSpace(text)) byText.TryAdd(text.Trim(), id);
        foreach (var row in rows)
            if (uptickId(row) == null && externalId(row) is { } text && byText.TryGetValue(text.Trim(), out var id))
                map.TryAdd(id, row);
        return map;
    }
}
