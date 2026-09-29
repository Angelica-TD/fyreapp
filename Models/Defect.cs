using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A defect / non-conformance raised against a property or asset (Uptick "Remark")
public class Defect
{
    public int Id { get; set; }

    // Maps from Uptick remark export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    // e.g. "00 - Asset Failed"
    public string? RemarkType { get; set; }

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
