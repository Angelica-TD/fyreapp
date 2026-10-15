using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

// Completing a routine task completes its routines. A routine done late (after its tolerance, or after its due month
// when it has none) moves the property's later routines of the same type on by as many months, so the next one falls
// an interval after the work was actually done: six-monthly due August, done November → the next one moves from
// February to May.
public static class RoutineCompletion
{
    public record Result(int Completed, int Moved, int MonthsLate);

    public static async Task<Result> CompleteAsync(AppDbContext db, ClientTask task, DateTime completed, CancellationToken ct = default)
    {
        var done = DateTime.SpecifyKind(completed.Date, DateTimeKind.Utc);

        // Routines the task was generated from; for an imported Uptick task, the property's raised routines its
        // scope of works names that are due in the task's month
        var routines = await db.RoutineOccurrences
            .Include(o => o.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
            .Where(o => o.ClientTaskId == task.Id && o.Status == RoutineOccurrenceStatus.Generated)
            .ToListAsync(ct);
        if (routines.Count == 0)
            routines = await InScopeAsync(db, db.RoutineOccurrences
                .Include(o => o.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
                .Where(o => o.ClientTaskId == null && o.Status == RoutineOccurrenceStatus.Generated), task, ct);

        var moved = 0;
        var monthsLate = 0;
        foreach (var o in routines)
        {
            o.Status = RoutineOccurrenceStatus.Complete;
            o.CompletedDate = done;
            o.ClientTaskId ??= task.Id;
        }

        // Each routine type once, by its latest-due routine on the task
        foreach (var o in routines.GroupBy(TypeOf).Select(g => g.MaxBy(o => o.DueDate)!))
        {
            var late = MonthsLate(o.DueDate, o.ToleranceEnd, done);
            if (late <= 0) continue;
            monthsLate = Math.Max(monthsLate, late);

            var type = TypeOf(o);
            var later = (await db.RoutineOccurrences
                    .Include(x => x.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
                    .Where(x => x.SiteId == o.SiteId && x.Status == RoutineOccurrenceStatus.Pending && x.ClientTaskId == null
                                && x.DueDate > o.DueDate)
                    .ToListAsync(ct))
                .Where(x => string.Equals(TypeOf(x), type, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var x in later)
            {
                x.DueDate = AddMonths(x.DueDate, late);
                x.ToleranceStart = x.ToleranceStart is DateTime s ? AddMonths(s, late) : null;
                x.ToleranceEnd = x.ToleranceEnd is DateTime e ? AddMonths(e, late) : null;
            }
            moved += later.Count;

            // Routine services set up in FyreApp count from their last annual date: move it too, so editing the
            // service later forecasts from when the work was done rather than undoing the move
            var serviceIds = later.Append(o).Select(x => x.PropertyRoutineServiceId).OfType<int>().Distinct().ToList();
            foreach (var service in await db.PropertyRoutineServices.Where(s => serviceIds.Contains(s.Id)).ToListAsync(ct))
                service.LastAnnualDate = AddMonths(service.LastAnnualDate, late);
        }

        return new Result(routines.Count, moved, monthsLate);
    }

    // For an imported Uptick task: the property's routines (of those given) its scope of works names that are due in
    // the task's month
    public static async Task<List<RoutineOccurrence>> InScopeAsync(
        AppDbContext db, IQueryable<RoutineOccurrence> occurrences, ClientTask task, CancellationToken ct = default)
    {
        if (task.DueDateUtc is not DateTime taskDue || UptickJson.Value(task.UptickData, "Scope of works") is not { } scope)
            return [];

        var names = RoutineAssets.FromScope(task.SiteId, scope, await db.RoutineServiceTypes.Select(t => t.Name).ToListAsync(ct))
            .Select(r => r.Routine).ToList();
        if (names.Count == 0) return [];

        var monthStart = new DateTime(taskDue.Year, taskDue.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        return (await occurrences
                .Where(o => o.SiteId == task.SiteId && o.DueDate >= monthStart && o.DueDate < monthEnd)
                .ToListAsync(ct))
            .Where(o => names.Contains(o.Routine, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    // Whole months between the due month and the month it was done, when it was done after its tolerance
    // (or after its due month, with no tolerance); otherwise 0
    public static int MonthsLate(DateTime due, DateTime? toleranceEnd, DateTime done)
    {
        var lastOnTime = toleranceEnd?.Date ?? new DateTime(due.Year, due.Month, DateTime.DaysInMonth(due.Year, due.Month));
        if (done.Date <= lastOnTime) return 0;
        return (done.Year * 12 + done.Month) - (due.Year * 12 + due.Month);
    }

    // The routine service type: from its level, else its name ("<type>: <level>")
    private static string TypeOf(RoutineOccurrence o) =>
        o.RoutineServiceLevel?.RoutineServiceType.Name ?? RoutineAssets.RoutineType(o.Routine);

    // Month-end dates stay month-end
    private static DateTime AddMonths(DateTime d, int months)
    {
        var moved = d.AddMonths(months);
        return d.Day == DateTime.DaysInMonth(d.Year, d.Month)
            ? DateTime.SpecifyKind(new DateTime(moved.Year, moved.Month, DateTime.DaysInMonth(moved.Year, moved.Month)), d.Kind)
            : moved;
    }
}
