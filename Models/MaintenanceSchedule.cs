using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public enum ScheduleTargetType
{
    Site = 1,
    Asset = 2
}

public class MaintenanceSchedule
{
    public int Id { get; set; }

    // FyreApp ref (e.g. "MS-1001"), assigned by the database. Uptick imports merge many
    // occurrence rows into one schedule, so there's no single Uptick ID to keep.
    [StringLength(20)]
    public string? FyreRef { get; set; }

    public string? DisplayRef => FyreRef;

    [Required]
    public ScheduleTargetType TargetType { get; set; }

    // Either SiteId or AssetId will be set (not both)
    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    // Interval
    public int MaintenanceIntervalId { get; set; }
    public MaintenanceInterval MaintenanceInterval { get; set; }

    public DateTime NextRunDate { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<MaintenanceHistory> MaintenanceHistory { get; set; }
    = new List<MaintenanceHistory>();

    // ClientTasks generated from this schedule's due occurrences
    public ICollection<ClientTask> GeneratedTasks { get; set; } = new List<ClientTask>();

    // Imported Uptick tasks whose scope covers this schedule (see ClientTask.CoveredSchedules)
    public ICollection<ClientTask> CoveringTasks { get; set; } = new List<ClientTask>();

}
