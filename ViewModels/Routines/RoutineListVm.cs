using FyreApp.Models;

namespace FyreApp.ViewModels.Routines;

// Filters for the Routines page. Defaults match Uptick's Routines list: active clients, due this month,
// active properties, pending (no task raised yet).
public class RoutineFilter
{
    public string? Search { get; set; }

    // true = active clients only, false = inactive only, null = any
    public bool? ClientActive { get; set; } = true;

    public DateTime? DueFrom { get; set; }
    public DateTime? DueTo { get; set; }

    // Empty = any
    public List<string> PropertyStatus { get; set; } = new() { "ACTIVE" };
    public List<RoutineOccurrenceStatus> Status { get; set; } = new() { RoutineOccurrenceStatus.Pending };

    public int Page { get; set; } = 1;

    // Uptick-style defaults; due dates span the current month (Sydney time)
    public static RoutineFilter Default(DateTime today)
    {
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return new RoutineFilter { DueFrom = monthStart, DueTo = monthStart.AddMonths(1).AddDays(-1) };
    }

    // How many filters narrow the list (shown on the Filters button, as Uptick does)
    public int ActiveCount =>
        (ClientActive != null ? 1 : 0) +
        (DueFrom != null || DueTo != null ? 1 : 0) +
        (PropertyStatus.Count > 0 ? 1 : 0) +
        (Status.Count > 0 ? 1 : 0);
}

public class RoutineListItemVm
{
    public int Id { get; set; }
    public string Routine { get; set; } = string.Empty;
    public int SiteId { get; set; }
    public string? PropertyRef { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientContact { get; set; }
    public DateTime DueDate { get; set; }
    public RoutineOccurrenceStatus Status { get; set; }
    public int? MaintenanceScheduleId { get; set; }
    public int? ClientTaskId { get; set; }
}

public class RoutineListVm
{
    public const int PageSize = 50;

    public RoutineFilter Filter { get; set; } = new();
    public IReadOnlyList<RoutineListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
