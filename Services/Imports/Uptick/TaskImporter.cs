using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Tasks" export -> ClientTask, matched to Site on "Property Ref".
// Routine (I&T) tasks are linked to the property's schedules for the frequencies in their "Scope of works",
// so FyreApp sees which schedules Uptick has already raised a task for. A completed one that covers a
// schedule's next occurrence rolls the schedule forward, as completing it in FyreApp would.
public class TaskImporter : UptickImporter
{
    public TaskImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.Tasks;
    public override string DisplayName => "Tasks";
    public override string[] SignatureHeaders => new[] { "Ref", "Category", "Scope of works", "Property Ref", "Due" };

    public const string NoPropertySiteName = "No property (Uptick task)";

    // Guards against runaway loops if a schedule's interval is odd; far more than any real backlog
    private const int MaxRollForwards = 24;

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var sitesByRef = await LoadSiteIdsByRefAsync(ct);
        var existing = (await Db.ClientTasks.AsNoTracking()
                .Where(t => t.ExternalId != null)
                .Select(t => t.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newRows = rows.Where(r => !existing.Contains(r.Get("ID") ?? "")).ToList();
        await AddPlaceholderSitesAsync(newRows, sitesByRef, "Property Name", dryRun, ctx, ct);

        // Placeholder ids are negative in a dry run; the client only matters when saving
        var clientIdBySite = await Db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.ClientId, ct);

        // Site schedules by (site, interval months), tracked so they can be linked and rolled forward
        var schedules = await Db.MaintenanceSchedules
            .Include(s => s.MaintenanceInterval)
            .Where(s => s.TargetType == ScheduleTargetType.Site && s.SiteId != null && s.IsActive)
            .ToListAsync(ct);
        var schedulesBySiteMonths = schedules.ToLookup(s => (s.SiteId!.Value, s.MaintenanceInterval.Months));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<ClientTask>();
        int? noPropertySiteId = null;
        var linked = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (!ClaimExternalId(id, existing, seen, row, ctx)) continue;

            var propertyRef = row.Get("Property Ref");
            int siteId;
            if (propertyRef != null)
            {
                siteId = sitesByRef[propertyRef]; // every ref is in FyreApp, or got a placeholder above
            }
            else
            {
                // A few Uptick tasks have no property; keep them on one placeholder property
                if (noPropertySiteId == null)
                {
                    var (npId, npClientId) = await GetNoPropertySiteAsync(dryRun, ct);
                    noPropertySiteId = npId;
                    clientIdBySite[npId] = npClientId;
                }
                siteId = noPropertySiteId.Value;
                ctx.Issue("No property", row.Get("Ref") ?? id!, row.RowNumber,
                    $"Task has no property in Uptick. Imported under the placeholder property '{NoPropertySiteName}'.");
            }

            var task = new ClientTask
            {
                ExternalId = id,
                Ref = row.Get("Ref"),
                UptickData = row.ToJson(),
                SiteId = siteId,
                ClientId = clientIdBySite.GetValueOrDefault(siteId),
                Title = Truncate(row.Get("Name") ?? FirstLine(row.Get("Description")) ?? row.Get("Ref") ?? id!, 120),
                Description = BuildDescription(row.Get("Description"), row.Get("Scope of works")),
                Status = MapStatus(row.Get("Status")),
                Priority = MapPriority(row.GetInt("Priority")),
                DueDateUtc = row.GetDate("Due"),
                CreatedUtc = row.GetTimestampUtc("Created") ?? DateTime.UtcNow,
                CompletedUtc = row.GetTimestampUtc("Completed Date")
            };

            if (string.Equals(row.Get("Category"), "I&T", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var months in ScopeMonths(row.Get("Scope of works")))
                foreach (var schedule in schedulesBySiteMonths[(siteId, months)])
                    task.CoveredSchedules.Add(schedule);

                if (task.CoveredSchedules.Count > 0) linked++;
            }

            toCreate.Add(task);
        }

        var rolled = RollSchedulesForward(toCreate, dryRun);

        ctx.Result.Created = toCreate.Count;
        if (linked > 0)
            ctx.Result.Notes.Add($"{linked} routine task{(linked == 1 ? "" : "s")} {(dryRun ? "will be" : "were")} linked to their property's maintenance schedules.");
        if (rolled > 0)
            ctx.Result.Notes.Add($"{rolled} schedule occurrence{(rolled == 1 ? "" : "s")} already completed in Uptick {(dryRun ? "will be" : "were")} marked done, moving those schedules to their next occurrence.");

        if (dryRun)
        {
            // Nothing is saved; don't leave links on the tracked schedules
            Db.ChangeTracker.Clear();
            return;
        }

        Db.ClientTasks.AddRange(toCreate);
        await Db.SaveChangesAsync(ct);
    }

    // For each linked schedule, while a completed imported task covers its next occurrence, log it in
    // maintenance history and move to the following occurrence. Returns how many occurrences were done.
    private int RollSchedulesForward(IReadOnlyList<ClientTask> tasks, bool dryRun)
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

                if (!dryRun)
                {
                    Db.MaintenanceHistory.Add(new MaintenanceHistory
                    {
                        MaintenanceSchedule = schedule,
                        CompletedAt = done.CompletedUtc ?? done.DueDateUtc ?? due,
                        DueDateAtCompletion = due,
                        Notes = $"Completed in Uptick ({done.Ref})"
                    });
                }

                next = due.Date.AddMonths(months);
                rolled++;
            }

            if (!dryRun) schedule.NextRunDate = DateTime.SpecifyKind(next, DateTimeKind.Utc);
        }

        return rolled;
    }

    private async Task<(int Id, int ClientId)> GetNoPropertySiteAsync(bool dryRun, CancellationToken ct)
    {
        var site = await Db.Sites.FirstOrDefaultAsync(s => s.IsPlaceholder && s.ExternalId == null && s.Name == NoPropertySiteName, ct);
        if (site != null) return (site.Id, site.ClientId);
        if (dryRun) return (int.MinValue, 0);

        site = new Site { Name = NoPropertySiteName, IsPlaceholder = true, Active = false, Client = await GetUnassignedClientAsync(ct) };
        Db.Sites.Add(site);
        await Db.SaveChangesAsync(ct);
        return (site.Id, site.ClientId);
    }

    // "- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual (1)" -> 12, one per line
    public static IReadOnlyList<int> ScopeMonths(string? scope) =>
        (scope ?? "").Split('\n')
            .Select(l => Regex.Replace(l.Trim().TrimStart('-', ' '), @"\s*\(\d+\)\s*$", ""))
            .Where(l => l.Contains(':'))
            .Select(l => ScheduleImportService.ParseFrequency(l).Months)
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
    private static ClientTaskPriority MapPriority(int? priority) => priority switch
    {
        null or 5 => ClientTaskPriority.Normal,
        < 5 => ClientTaskPriority.High,
        _ => ClientTaskPriority.Low
    };

    private static string? BuildDescription(string? description, string? scope)
    {
        var text = scope == null || string.Equals(scope, description, StringComparison.Ordinal)
            ? description
            : string.IsNullOrWhiteSpace(description) ? $"Scope of works:\n{scope}" : $"{description}\n\nScope of works:\n{scope}";
        return text == null ? null : Truncate(text, 4000);
    }

    private static string? FirstLine(string? s) =>
        s?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
