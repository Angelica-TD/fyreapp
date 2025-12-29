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

}
