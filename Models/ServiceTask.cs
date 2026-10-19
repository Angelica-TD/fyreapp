using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// An asset a task services for a routine, e.g. extinguisher "FE 3" for "10 - Portable and Wheeled Fire Extinguishers:
// Six-monthly" (the task's Work tab): Uptick's service tasks, with the result, or saved when FyreApp generates the
// task from routines, so the list stays as it was planned.
public class ServiceTask : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for service tasks migrated through its API
    public long? UptickId { get; set; }

    public int ClientTaskId { get; set; }
    public ClientTask ClientTask { get; set; } = null!;

    // None for a line of stock used rather than an asset serviced
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    // The routine the asset is serviced for, as Uptick names it ("<routine service type>: <level>")
    [Required, StringLength(300)]
    public string Routine { get; set; } = string.Empty;

    public int? RoutineServiceLevelId { get; set; }
    public RoutineServiceLevel? RoutineServiceLevel { get; set; }

    // Pass, Fail or N/A once serviced
    [StringLength(10)]
    public string? Result { get; set; }

    public DateTime? PerformedUtc { get; set; }

    public int? TechnicianId { get; set; }
    public UptickUser? Technician { get; set; }

    public string? Notes { get; set; }

    // What it points to in Uptick that FyreApp maps in a later phase (routine, remark, product)
    public long? UptickRoutineId { get; set; }
    public long? UptickRemarkId { get; set; }
    public long? UptickProductId { get; set; }
}
