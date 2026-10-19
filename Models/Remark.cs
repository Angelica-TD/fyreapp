using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A defect / non-conformance raised against a property or asset (Uptick "Remark")
public class Remark : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Maps from Uptick remark export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "D-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Every column of the Uptick export row as JSON (key order kept), so nothing is lost on import
    public string? UptickData { get; set; }

    // Shown to users: the Uptick ID if imported, otherwise the FyreApp ref
    // Uptick shows remarks as "D-<ID>"
    public string? DisplayRef => string.IsNullOrWhiteSpace(ExternalId) ? FyreRef : $"D-{ExternalId}";

    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;

    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    // e.g. "00 - Asset Failed"
    public string? RemarkType { get; set; }

    // The Uptick remark type ("Remark Type ID"), when known
    public int? RemarkTypeId { get; set; }
    public RemarkType? RemarkTypeRef { get; set; }

    // e.g. "Needs Quoting"
    public string? Status { get; set; }

    public int? Severity { get; set; }

    // e.g. "Non-conformance"
    public string? SeverityLabel { get; set; }

    public string? Description { get; set; }
    public string? Resolution { get; set; }
    public string? Notes { get; set; }
    public string? Location { get; set; }

    public bool Active { get; set; } = true;

    public DateTime? RaisedUtc { get; set; }

    // When it was closed off, e.g. fixed or replaced (Uptick "Inactive Date")
    public DateTime? InactiveUtc { get; set; }

    // When it was quoted (Uptick "Quoted Date", a calendar date)
    public DateTime? QuotedDate { get; set; }

    // Uptick task / quote references
    public string? RaisedOnTaskRef { get; set; }
    public string? QuoteRef { get; set; }
    public string? QuoteStatus { get; set; }
    public string? RepairTaskRef { get; set; }
    public string? RepairTaskStatus { get; set; }

    // The task raised to repair it: Uptick's task ID (export "Repair Task ID"), linked to the imported task
    // once both are in FyreApp (whichever is imported second makes the link)
    [StringLength(64)]
    public string? RepairTaskExternalId { get; set; }
    public int? RepairTaskId { get; set; }
    public ClientTask? RepairTask { get; set; }
}
