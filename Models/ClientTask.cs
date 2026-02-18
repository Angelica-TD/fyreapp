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
    public string? CreatedByUserId { get; set; }
}
