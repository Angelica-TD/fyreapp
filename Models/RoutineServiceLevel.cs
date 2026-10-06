using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// How often a routine is done, e.g. "Six-monthly" for "10 - Portable and Wheeled Fire Extinguishers"
// (Uptick routine service level types export). The routines export names an occurrence "<routine>: <level>".
public class RoutineServiceLevel
{
    public int Id { get; set; }

    // Maps from Uptick routine service level types export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    public int RoutineServiceTypeId { get; set; }
    public RoutineServiceType RoutineServiceType { get; set; } = null!;

    // e.g. "Six-monthly"
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    // e.g. "H"
    [StringLength(20)]
    public string? DisplayCode { get; set; }

    // Uptick's configured interval ("Interval" with "Frequency" = M)
    public int IntervalMonths { get; set; }

    public int? OffsetMonths { get; set; }

    // How early/late the work can be done, e.g. 1 M or 14 D
    public int? ToleranceInterval { get; set; }

    [StringLength(10)]
    public string? ToleranceUnit { get; set; }

    public bool DefaultEnabled { get; set; }
    public bool AssetBased { get; set; }
    public bool SupersedesLowerRank { get; set; }
    public int? ServiceRank { get; set; }
    public bool Custom { get; set; }
    public bool Active { get; set; } = true;

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }

    // Schedules (property + interval) that include this routine level
    public ICollection<MaintenanceSchedule> Schedules { get; set; } = new List<MaintenanceSchedule>();
}
