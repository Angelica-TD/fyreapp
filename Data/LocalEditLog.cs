using System.Globalization;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FyreApp.Data;

// Records changes FyreApp makes to fields of records migrated from Uptick as LocalEdits, from the change
// tracker on save, so every form and bulk action is covered (docs/uptick-migration.md §7)
public static class LocalEditLog
{
    // An asset's type: Uptick gives one, held in the AssetAssetTypes join
    public const string AssetTypes = "AssetTypes";

    // Bookkeeping, not fields anyone edits
    private static readonly HashSet<string> NotFields = new(StringComparer.Ordinal)
    {
        "Id", "UptickId", "FyreRef", "IsPlaceholder", "Created", "Updated"
    };

    public static void Record(AppDbContext db, string? by, DateTime now)
    {
        var changes = new List<(string Entity, int RecordId, string Field, string? Before, string? After)>();

        foreach (var entry in db.ChangeTracker.Entries<IUptickMapped>())
        {
            if (entry.State != EntityState.Modified || entry.Entity.UptickId == null) continue;
            if (entry.Property("Id").CurrentValue is not int id) continue;
            foreach (var p in entry.Properties)
            {
                if (!p.IsModified || NotFields.Contains(p.Metadata.Name)) continue;
                var before = Text(p.OriginalValue);
                var after = Text(p.CurrentValue);
                if (before != after) changes.Add((entry.Metadata.ClrType.Name, id, p.Metadata.Name, before, after));
            }
        }
        changes.AddRange(AssetTypeChanges(db));
        if (changes.Count == 0) return;

        var ids = changes.Select(c => c.RecordId).Distinct().ToList();
        var entities = changes.Select(c => c.Entity).Distinct().ToList();
        var open = db.LocalEdits
            .Where(e => e.ClosedUtc == null && entities.Contains(e.Entity) && ids.Contains(e.RecordId))
            .ToList()
            .ToDictionary(e => (e.Entity, e.RecordId, e.Field));

        foreach (var (entity, recordId, field, before, after) in changes)
        {
            if (!open.TryGetValue((entity, recordId, field), out var edit))
            {
                // The value it had is Uptick's: no FyreApp edit of this field is open
                db.LocalEdits.Add(new LocalEdit
                {
                    Entity = entity, RecordId = recordId, Field = field, UptickValue = before, FyreAppValue = after,
                    EditedBy = Cap(by), EditedUtc = now
                });
                continue;
            }

            if (after == edit.UptickValue)
            {
                Close(edit, "Reverted", by, now);
                continue;
            }
            edit.FyreAppValue = after;
            edit.EditedBy = Cap(by);
            edit.EditedUtc = now;
        }
    }

    // A migrated asset whose type was swapped: the join row removed for the old type and added for the new
    private static IEnumerable<(string, int, string, string?, string?)> AssetTypeChanges(AppDbContext db)
    {
        var joins = db.ChangeTracker.Entries()
            .Where(e => e.Metadata.Name == "AssetAssetType" && e.State is EntityState.Added or EntityState.Deleted)
            .Select(e => (Asset: (int)e.Property("AssetsId").CurrentValue!, Type: (int)e.Property("AssetTypesId").CurrentValue!, Added: e.State == EntityState.Added))
            .ToList();
        foreach (var g in joins.GroupBy(j => j.Asset))
        {
            var asset = db.ChangeTracker.Entries<Asset>().FirstOrDefault(a => a.Entity.Id == g.Key);
            if (asset?.Entity.UptickId == null || asset.State == EntityState.Added) continue;
            var before = g.FirstOrDefault(j => !j.Added).Type is var b and > 0 ? Text(b) : null;
            var after = g.FirstOrDefault(j => j.Added).Type is var a and > 0 ? Text(a) : null;
            if (before != after) yield return (nameof(Asset), g.Key, AssetTypes, before, after);
        }
    }

    public static void Close(LocalEdit edit, string reason, string? by, DateTime now)
    {
        edit.ClosedUtc = now;
        edit.ClosedReason = reason;
        edit.ClosedBy = Cap(by);
    }

    // A value as LocalEdit stores it
    public static string? Text(object? value) => value switch
    {
        null => null,
        string s => s,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    // The stored text back as the property's type, to put a value back
    public static object? Parse(string? text, Type type)
    {
        if (text == null) return null;
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return text;
        if (t == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(bool)) return text == "true";
        // Kept by name (a task's status: "Open")
        if (t.IsEnum) return Enum.Parse(t, text);
        return Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
    }

    // Puts a migrated record's field back to Uptick's value and closes the edit
    public static void Reset(AppDbContext db, LocalEdit edit, object record, string? by, DateTime now)
    {
        var entry = db.Entry(record);
        if (edit.Field != AssetTypes)
        {
            var p = entry.Property(edit.Field);
            p.CurrentValue = Parse(edit.UptickValue, p.Metadata.ClrType);
        }
        else if (record is Asset asset)
        {
            asset.AssetTypes.Clear();
            if (edit.UptickValue != null && db.AssetTypes.Find(int.Parse(edit.UptickValue, CultureInfo.InvariantCulture)) is { } type)
                asset.AssetTypes.Add(type);
        }
        Close(edit, "Reset", by, now);
    }

    private static string? Cap(string? s) => s?.Length > 200 ? s[..200] : s;
}
