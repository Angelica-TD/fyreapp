using FyreApp.Models;

namespace FyreApp.Services.MaintenanceSchedules;

// When a task counts as the task for a schedule's next occurrence. Tasks generated in FyreApp are due
// exactly on NextRunDate; Uptick tasks are due near it (routines fall on the 1st, their task is due
// e.g. on the 30th, or the last day of the month before), so allow a window around it.
public static class ScheduleCoverage
{
    public const int DaysBefore = 7;
    public const int DaysAfter = 31;

    public static bool Covers(ClientTask task, DateTime nextRunDate) =>
        task.Status != ClientTaskStatus.Cancelled &&
        task.DueDateUtc is { } due &&
        due >= nextRunDate.AddDays(-DaysBefore) &&
        due <= nextRunDate.AddDays(DaysAfter);
}
