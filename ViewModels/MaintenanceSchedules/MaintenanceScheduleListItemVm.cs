using FyreApp.Models;

namespace FyreApp.ViewModels.MaintenanceSchedules;

public enum ScheduleWindow { Month, Overdue, All }
public enum ScheduleGenerationStatus { Pending, Generated, All }

public class MaintenanceScheduleFilter
{
    public ScheduleWindow Window { get; set; } = ScheduleWindow.Month;
    public ScheduleTargetType? TargetType { get; set; }
    public ScheduleGenerationStatus GenerationStatus { get; set; } = ScheduleGenerationStatus.Pending;
}

public class MaintenanceScheduleListItemVm
{
    public int Id { get; set; }
    public string? DisplayRef { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string TargetLabel { get; set; } = string.Empty;
    public string IntervalName { get; set; } = string.Empty;
    public DateTime NextRunDate { get; set; }
    public bool IsOverdue { get; set; }
    public int? GeneratedTaskId { get; set; }
    public ClientTaskStatus? GeneratedTaskStatus { get; set; }
}

public class MaintenanceScheduleIndexVm
{
    public IReadOnlyList<MaintenanceScheduleListItemVm> Schedules { get; set; } = [];
    public ScheduleWindow Window { get; set; }
    public ScheduleTargetType? TargetType { get; set; }
    public ScheduleGenerationStatus GenerationStatus { get; set; }
}
