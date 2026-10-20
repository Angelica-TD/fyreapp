using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A defect / non-conformance raised against a property or asset (Uptick "Remark")
public class Remark : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // FyreApp ref (e.g. "D-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: the Uptick ID if migrated, otherwise the FyreApp ref
    // Uptick shows remarks as "D-<ID>"
    public string? DisplayRef => UptickId is { } id ? $"D-{id}" : FyreRef;

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

    // The task it was raised on (Uptick's "Created on Task")
    public int? RaisedOnTaskId { get; set; }
    public ClientTask? RaisedOnTask { get; set; }

    // Uptick quote reference
    public string? QuoteRef { get; set; }

    // Its most progressed defect quote in Uptick
    public int? DefectQuoteId { get; set; }
    public DefectQuote? DefectQuote { get; set; }
    public string? QuoteStatus { get; set; }
    public string? RepairTaskRef { get; set; }
    public string? RepairTaskStatus { get; set; }

    public int? RepairTaskId { get; set; }
    public ClientTask? RepairTask { get; set; }
}
