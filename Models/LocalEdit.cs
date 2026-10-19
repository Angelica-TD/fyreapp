using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A record migrated from Uptick (UptickId set)
public interface IUptickMapped
{
    long? UptickId { get; }
}

// A field of a migrated record changed in FyreApp (docs/uptick-migration.md §7). Mapping keeps FyreApp's value
// while Uptick's is still the one it had when the edit was made; once Uptick changes it, Uptick's wins and the edit
// is closed (kept for history).
public class LocalEdit
{
    public int Id { get; set; }

    // The entity class and its row, e.g. ("Asset", 14269)
    [StringLength(50)] public string Entity { get; set; } = string.Empty;
    public int RecordId { get; set; }

    // The property, e.g. "Location" ("AssetTypes" for an asset's type, by AssetType id)
    [StringLength(100)] public string Field { get; set; } = string.Empty;

    // As invariant text (dates ISO 8601), null for no value
    public string? FyreAppValue { get; set; }
    public string? UptickValue { get; set; }

    [StringLength(200)] public string? EditedBy { get; set; }
    public DateTime EditedUtc { get; set; }

    public DateTime? ClosedUtc { get; set; }

    // Uptick (changed there since), Reset (put back to Uptick's by an admin), Reverted (edited back to Uptick's)
    [StringLength(20)] public string? ClosedReason { get; set; }

    [StringLength(200)] public string? ClosedBy { get; set; }

    public bool IsOpen => ClosedUtc == null;
}
