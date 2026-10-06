using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A defect / non-conformance raised against a property or asset (Uptick "Remark")
public class Defect
{
    public int Id { get; set; }

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

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

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

    // Uptick task / quote references
    public string? RaisedOnTaskRef { get; set; }
    public string? QuoteRef { get; set; }
    public string? QuoteStatus { get; set; }
    public string? RepairTaskRef { get; set; }
    public string? RepairTaskStatus { get; set; }
}
