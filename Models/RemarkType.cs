using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// Kind of defect Uptick technicians raise, e.g. "1.02 - Battery missing indication" (Uptick remark types export)
public class RemarkType : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Maps from Uptick remark types export "ID" (remarks carry it as "Remark Type ID")
    [StringLength(64)]
    public string? ExternalId { get; set; }

    [Required, StringLength(200)]
    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? AssetTypeId { get; set; }
    public AssetType? AssetType { get; set; }

    // e.g. "Wet Systems"
    [StringLength(100)]
    public string? AssetTypeTag { get; set; }

    // e.g. "Critical defect", "Non-conformance"
    [StringLength(100)]
    public string? SeverityLabel { get; set; }

    // Standard fix, e.g. "Supply and install new batteries"
    public string? Resolution { get; set; }

    public bool OwnerResponsible { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<Remark> Remarks { get; set; } = new List<Remark>();
}
