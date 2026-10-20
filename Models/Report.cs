using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A service/inspection report issued for a property (Uptick "Report")
public class Report : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;

    // e.g. "R-51081"
    public string? Ref { get; set; }

    // FyreApp ref (e.g. "SR-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: Uptick's report ref (what's printed on the report), then its ID, otherwise the FyreApp ref
    public string? DisplayRef =>
        !string.IsNullOrWhiteSpace(Ref) ? Ref
        : UptickId?.ToString() ?? FyreRef;

    // e.g. "Service Report"
    public string? ReportType { get; set; }

    public DateTime? IssuedDate { get; set; }
    public DateTime? InspectedDate { get; set; }

    public bool? Compliant { get; set; }

    public string? TaskRef { get; set; }
    public string? TaskName { get; set; }

    public int? ClientTaskId { get; set; }
    public ClientTask? ClientTask { get; set; }
    public string? Technician { get; set; }
    public string? Author { get; set; }

    public bool Published { get; set; }
    public bool Amendment { get; set; }

    public string? GeneralNote { get; set; }
    public string? CriticalNote { get; set; }
}

// One asset's line on a report in Uptick ("reportitems"): the asset as the report showed it, and whether it passed
public class ReportItem : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    // As printed on the report, e.g. "019 - DCP AB(E) 4.5KG Portable Fire Extinguisher"
    [StringLength(500)] public string? Label { get; set; }
    [StringLength(100)] public string? Ref { get; set; }
    [StringLength(500)] public string? Location { get; set; }
    [StringLength(100)] public string? Barcode { get; set; }
    [StringLength(200)] public string? Make { get; set; }
    [StringLength(200)] public string? Model { get; set; }
    [StringLength(200)] public string? Size { get; set; }
    [StringLength(200)] public string? Standard { get; set; }

    public bool? Compliant { get; set; }
    public DateTime? InspectedDate { get; set; }
    public DateTime? ServicedDate { get; set; }
    public DateTime? BaseDate { get; set; }
}
