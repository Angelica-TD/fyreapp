using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public enum ClientTaskStatus
{
    Open = 1,
    InProgress = 2,
    Blocked = 3,
    Completed = 4,
    Cancelled = 5
}

public enum ClientTaskPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4
}

public class ClientTask
{
    public int Id { get; set; }

    // Maps from Uptick task export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // Uptick task ref, e.g. "T-46616"
    [StringLength(20)]
    public string? Ref { get; set; }

    // FyreApp ref (e.g. "FT-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Every column of the Uptick export row as JSON (key order kept), so nothing is lost on import
    public string? UptickData { get; set; }

    // Shown to users: Uptick's task ref, then its ID, otherwise the FyreApp ref
    public string? DisplayRef =>
        !string.IsNullOrWhiteSpace(Ref) ? Ref
        : !string.IsNullOrWhiteSpace(ExternalId) ? ExternalId
        : FyreRef;

    // Required links
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    // "Property" = Site
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public ClientTaskStatus Status { get; set; } = ClientTaskStatus.Open;
    public ClientTaskPriority Priority { get; set; } = ClientTaskPriority.Normal;

    public DateTime? DueDateUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }

    public string? AssignedToUserId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }
    public string? CreatedByUserId { get; set; }

    // Set when this task was auto-generated from a routine MaintenanceSchedule
    public int? MaintenanceScheduleId { get; set; }
    public MaintenanceSchedule? MaintenanceSchedule { get; set; }

    // Schedules an imported Uptick routine (I&T) task covers. One Uptick task can cover several
    // frequencies at a property (e.g. six-monthly and annual), so this is many-to-many.
    public ICollection<MaintenanceSchedule> CoveredSchedules { get; set; } = new List<MaintenanceSchedule>();
}
