using System;

namespace FyreApp.Models;

public class MaintenanceHistory
{
    public int Id { get; set; }

    public int MaintenanceScheduleId { get; set; }
    public MaintenanceSchedule MaintenanceSchedule { get; set; } = null!;

    /// <summary>
    /// When the maintenance work was actually completed
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// What the due date was at the time of completion
    /// (important if intervals change later)
    /// </summary>
    public DateTime DueDateAtCompletion { get; set; }

    /// <summary>
    /// Optional notes entered by the technician/user
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Optional: user who completed the work
    /// </summary>
    public string? CompletedByUserId { get; set; }

}
