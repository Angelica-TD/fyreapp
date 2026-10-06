using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public enum RoutineOccurrenceStatus
{
    Pending = 1,     // Uptick "P": no task raised yet
    Generated = 2,   // Uptick "G": a task has been raised
    Complete = 3,    // Uptick "C"
    Cancelled = 4    // Uptick "X"
}

// One due occurrence of an Uptick routine at a property, e.g. "04 - Fire Hydrant Systems (Flow Test): Annual"
// due 31 Oct 2026 (one row of the Uptick routines export). Schedules summarise these per property + interval;
// the Routines page lists them as Uptick does. Re-importing the routines export updates their status.
public class RoutineOccurrence
{
    public int Id { get; set; }

    // Maps from Uptick routines export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    // "<routine service type>: <level>" as Uptick names it
    [Required, StringLength(300)]
    public string Routine { get; set; } = string.Empty;

    public int? RoutineServiceLevelId { get; set; }
    public RoutineServiceLevel? RoutineServiceLevel { get; set; }

    // The schedule (property + interval) this occurrence belongs to
    public int? MaintenanceScheduleId { get; set; }
    public MaintenanceSchedule? MaintenanceSchedule { get; set; }

    public DateTime DueDate { get; set; }
    public DateTime? ToleranceStart { get; set; }
    public DateTime? ToleranceEnd { get; set; }

    public RoutineOccurrenceStatus Status { get; set; } = RoutineOccurrenceStatus.Pending;
    public DateTime? CompletedDate { get; set; }

    // e.g. "Servicing - Portables & Fire Equipment"
    [StringLength(100)]
    public string? ServiceGroup { get; set; }

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }
}
