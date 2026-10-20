using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// Time a technician logged on a task in Uptick ("tasksessions"): travelling, on site, at the office…, with where
// they were when they started and finished
public class TaskSession : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int ClientTaskId { get; set; }
    public ClientTask ClientTask { get; set; } = null!;

    public int? UptickUserId { get; set; }
    public UptickUser? UptickUser { get; set; }

    // Uptick's session type, e.g. "Performing Task", "Travelling"; on site or not
    [StringLength(100)] public string? Type { get; set; }
    public bool IsOnsite { get; set; }

    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public decimal? Hours { get; set; }
    public decimal? Cost { get; set; }

    // e.g. "Normal Time", "Time and a half"
    [StringLength(100)] public string? Rate { get; set; }
    public string? Description { get; set; }
    public bool IsSubmitted { get; set; }
    public bool IsApproved { get; set; }

    public double? StartedLatitude { get; set; }
    public double? StartedLongitude { get; set; }
    public double? FinishedLatitude { get; set; }
    public double? FinishedLongitude { get; set; }

    // Thrown away in Uptick (it keeps them, marked)
    public DateTime? TrashedUtc { get; set; }
}

// A visit booked for a task in Uptick's scheduler ("appointments"), with the technicians booked
public class Appointment : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int? ClientTaskId { get; set; }
    public ClientTask? ClientTask { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    // scheduled, cancelled
    [StringLength(30)] public string? Status { get; set; }
    [StringLength(500)] public string? Subject { get; set; }
    public string? Notes { get; set; }
    [StringLength(500)] public string? Address { get; set; }

    // Uptick's appointment category, e.g. "Inflexible"
    [StringLength(100)] public string? Category { get; set; }

    public List<UptickUser> Technicians { get; set; } = [];
}

// One line of what a task is for in Uptick ("subtasks"): a routine due, or a product, and whether it's done, reported
// and billed
public class Subtask : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int ClientTaskId { get; set; }
    public ClientTask ClientTask { get; set; } = null!;

    public int Order { get; set; }

    public int? RoutineId { get; set; }
    public Routine? Routine { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public bool IsPerformed { get; set; }
    public DateTime? PerformedDate { get; set; }
    public bool IsServiceTask { get; set; }
    public bool IsReportTask { get; set; }
    public bool IsBillingTask { get; set; }

    // Removed from the task in Uptick (it keeps them, marked)
    public DateTime? DeletedUtc { get; set; }
}

// A movement of stock on a task in Uptick ("taskstocktransactions"): onto the task (allocated, or from a warehouse)
// when the quantity is positive, used on one of its service lines when negative. Which product, at what cost, by whom.
public class TaskStockTransaction : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int ClientTaskId { get; set; }
    public ClientTask ClientTask { get; set; } = null!;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    // Onto the task when positive (allocated, from a warehouse, or back from a service line); off it (used on a
    // service line) when negative
    public decimal Quantity { get; set; }

    // The other side of the movement, by Uptick record type and id: "warehouse" (taken from) or "servicetask" (used
    // on); none when stock was just allocated to the task
    [StringLength(50)] public string? OtherType { get; set; }
    public long? OtherUptickId { get; set; }

    public decimal? CostPrice { get; set; }

    public int? UptickUserId { get; set; }
    public UptickUser? UptickUser { get; set; }

    public DateTime? CreatedUtc { get; set; }
}
