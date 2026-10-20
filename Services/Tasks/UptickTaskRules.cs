using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Tasks;

// How an Uptick task becomes a ClientTask: its status, priority and description, the routine schedules it covers
// (rolled forward once it's completed), and the placeholder property for a task with none
public static class UptickTaskRules
{
    public const string NoPropertySiteName = "No property (Uptick task)";

    // Holds placeholder properties whose client isn't known
    public const string UnassignedClientName = "Unassigned (not in Uptick export)";

    // Guards against runaway loops if a schedule's interval is odd; far more than any real backlog
    private const int MaxRollForwards = 24;

    // Reports and remarks a file import placed before their task point at it by Uptick task ID; link them now
    public static async Task<(int Reports, int Remarks)> LinkWaitingRecordsAsync(AppDbContext db, IReadOnlyList<ClientTask> created, CancellationToken ct)
    {
        var idByExt = created.Where(t => t.ExternalId != null)
            .GroupBy(t => t.ExternalId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        int reports = 0, remarks = 0;

        foreach (var chunk in idByExt.Keys.Chunk(1000))
        {
            var exts = chunk.ToList();
            foreach (var r in await db.Reports.Where(r => r.ClientTaskId == null && r.TaskExternalId != null && exts.Contains(r.TaskExternalId)).ToListAsync(ct))
            {
                r.ClientTaskId = idByExt[r.TaskExternalId!];
                reports++;
            }
            foreach (var d in await db.Remarks.Where(d => d.RepairTaskId == null && d.RepairTaskExternalId != null && exts.Contains(d.RepairTaskExternalId)).ToListAsync(ct))
            {
                d.RepairTaskId = idByExt[d.RepairTaskExternalId!];
                remarks++;
            }
        }

        if (reports + remarks > 0)
            await db.SaveChangesAsync(ct);
        return (reports, remarks);
    }


    // For each linked schedule, while a completed Uptick task covers its next occurrence, log it in
    // maintenance history and move to the following occurrence. Returns how many occurrences were done.
    public static int RollSchedulesForward(AppDbContext db, IReadOnlyList<ClientTask> tasks)
    {
        var rolled = 0;
        var completedBySchedule = tasks
            .Where(t => t.Status == ClientTaskStatus.Completed)
            .SelectMany(t => t.CoveredSchedules.Select(s => (Schedule: s, Task: t)))
            .GroupBy(x => x.Schedule, x => x.Task);

        foreach (var group in completedBySchedule)
        {
            var schedule = group.Key;
            var months = schedule.MaintenanceInterval.Months;
            if (months <= 0) continue;

            var next = schedule.NextRunDate;
            for (var i = 0; i < MaxRollForwards; i++)
            {
                var due = next;
                var done = group.FirstOrDefault(t => ScheduleCoverage.Covers(t, due));
                if (done == null) break;

                db.MaintenanceHistory.Add(new MaintenanceHistory
                {
                    MaintenanceSchedule = schedule,
                    CompletedAt = done.CompletedUtc ?? done.DueDateUtc ?? due,
                    DueDateAtCompletion = due,
                    Notes = $"Completed in Uptick ({done.Ref})"
                });

                next = due.Date.AddMonths(months);
                rolled++;
            }

            schedule.NextRunDate = DateTime.SpecifyKind(next, DateTimeKind.Utc);
        }

        return rolled;
    }

    public static async Task<(int Id, int ClientId)> GetNoPropertySiteAsync(AppDbContext db, CancellationToken ct)
    {
        var site = await db.Properties.FirstOrDefaultAsync(s => s.IsPlaceholder && s.ExternalId == null && s.Name == NoPropertySiteName, ct);
        if (site != null) return (site.Id, site.ClientId);

        site = new Property { Name = NoPropertySiteName, IsPlaceholder = true, Active = false, Client = await UnassignedClientAsync(db, ct) };
        db.Properties.Add(site);
        await db.SaveChangesAsync(ct);
        return (site.Id, site.ClientId);
    }

    public static async Task<Client> UnassignedClientAsync(AppDbContext db, CancellationToken ct) =>
        await db.Clients.FirstOrDefaultAsync(c => c.IsPlaceholder && c.ExternalId == null && c.Name == UnassignedClientName, ct)
        ?? new Client { Name = UnassignedClientName, IsPlaceholder = true, Active = false, Created = DateTime.UtcNow };

    // "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual (1)" -> 12, one per line
    public static IReadOnlyList<int> ScopeMonths(string? scope) =>
        (scope ?? "").Split('\n')
            .Select(l => Regex.Replace(l.Trim().TrimStart('-', ' '), @"\s*\(\d+\)\s*$", ""))
            .Where(l => l.Contains(':'))
            .Select(l => RoutineRules.ParseFrequency(l).Months)
            .OfType<int>()
            .Distinct()
            .ToList();

    // Uptick statuses seen: READY, NOTREADY, SCHEDULED, PENDINGEXT, INPROGRESS, PERFORMED, OFFICEREVIEW,
    // CONTRACTORREVIEW, REVISIT, COMPLETE, CANCELLED
    public static ClientTaskStatus MapStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "COMPLETE" or "COMPLETED" => ClientTaskStatus.Completed,
        "CANCELLED" or "CANCELED" => ClientTaskStatus.Cancelled,
        "INPROGRESS" or "PERFORMED" or "OFFICEREVIEW" or "CONTRACTORREVIEW" or "REVISIT" => ClientTaskStatus.InProgress,
        _ => ClientTaskStatus.Open
    };

    // Uptick priority: 5 is the default; lower is more urgent
    public static ClientTaskPriority MapPriority(int? priority) => priority switch
    {
        null or 5 => ClientTaskPriority.Normal,
        < 5 => ClientTaskPriority.High,
        _ => ClientTaskPriority.Low
    };

    public static string? BuildDescription(string? description, string? scope)
    {
        var text = scope == null || string.Equals(scope, description, StringComparison.Ordinal)
            ? description
            : string.IsNullOrWhiteSpace(description) ? $"Scope of works:\n{scope}" : $"{description}\n\nScope of works:\n{scope}";
        return text == null ? null : Truncate(text, 4000);
    }

    public static string? FirstLine(string? s) =>
        s?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
