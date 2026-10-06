using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A service/inspection report issued for a property (Uptick "Report")
public class ServiceReport
{
    public int Id { get; set; }

    // Maps from Uptick report export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    // e.g. "R-51081"
    public string? Ref { get; set; }

    // FyreApp ref (e.g. "SR-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Every column of the Uptick export row as JSON (key order kept), so nothing is lost on import
    public string? UptickData { get; set; }

    // Shown to users: Uptick's report ref (what's printed on the report), then its ID, otherwise the FyreApp ref
    public string? DisplayRef =>
        !string.IsNullOrWhiteSpace(Ref) ? Ref
        : !string.IsNullOrWhiteSpace(ExternalId) ? ExternalId
        : FyreRef;

    // e.g. "Service Report"
    public string? ReportType { get; set; }

    public DateTime? IssuedDate { get; set; }
    public DateTime? InspectedDate { get; set; }

    public bool? Compliant { get; set; }

    public string? TaskRef { get; set; }
    public string? TaskName { get; set; }
    public string? Technician { get; set; }
    public string? Author { get; set; }

    public bool Published { get; set; }
    public bool Amendment { get; set; }

    public string? GeneralNote { get; set; }
    public string? CriticalNote { get; set; }
}
