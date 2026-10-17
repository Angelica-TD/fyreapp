using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "Tasks" export -> ClientTask, matched to Property on "Property Ref".
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
        var existing = ByExternalId(await Db.ClientTasks.AsNoTracking()
            .Where(t => t.ExternalId != null)
            .Select(t => new ImportedRecord(t.Id, t.ExternalId!, t.UptickData))
            .ToListAsync(ct));

        await AddPlaceholderSitesAsync(rows.Where(r => NewOrChanged(r, "ID", existing)).ToList(), sitesByRef, "Property Name", dryRun, ctx, ct);

        // Placeholder ids are negative in a dry run; the client only matters when saving
        var clientIdBySite = await Db.Properties.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.ClientId, ct);

        // Property schedules by (site, interval months), tracked so they can be linked and rolled forward
        var schedules = await Db.MaintenanceSchedules
            .Include(s => s.MaintenanceInterval)
            .Where(s => s.TargetType == ScheduleTargetType.Property && s.PropertyId != null && s.IsActive)
            .ToListAsync(ct);
        var schedulesBySiteMonths = schedules.ToLookup(s => (s.PropertyId!.Value, s.MaintenanceInterval.Months));

        // Uptick "Assigned Technician" names -> Uptick users
        var technicianIds = await TechnicianMatch.IdsByNameAsync(Db, ct);
        var unmatchedTechnicians = 0;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<ClientTask>();
        var toUpdate = new Dictionary<int, Action<ClientTask>>();
        int? noPropertySiteId = null;
        var linked = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            var action = Claim(id, existing, seen, row, ctx, out var existingId);
            if (action == RowAction.Skip) continue;

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

            // Routine (I&T) tasks cover the property's schedules for the frequencies in their scope
            var covered = string.Equals(row.Get("Category"), "I&T", StringComparison.OrdinalIgnoreCase)
                ? ScopeMonths(row.Get("Scope of works")).SelectMany(m => schedulesBySiteMonths[(siteId, m)]).Distinct().ToList()
                : new List<MaintenanceSchedule>();
            if (covered.Count > 0) linked++;
            var clientId = clientIdBySite.GetValueOrDefault(siteId);
            var technicianId = TechnicianMatch.Find(technicianIds, row.Get("Assigned Technician"));
            if (technicianId == null && row.Get("Assigned Technician") != null) unmatchedTechnicians++;

            if (action == RowAction.Create)
            {
                var task = new ClientTask { ExternalId = id };
                Map(task, row, siteId, clientId, covered, technicianId);
                toCreate.Add(task);
            }
            else
            {
                toUpdate[existingId] = t => Map(t, row, siteId, clientId, covered, technicianId);
            }
        }

        // Updated tasks are loaded (tracked) so a task now completed in Uptick rolls its schedules forward too.
        // In a dry run nothing is loaded; the counts are what matter.
        var updated = new List<ClientTask>();
        if (!dryRun)
            await ApplyUpdatesAsync(Db.ClientTasks.Include(t => t.CoveredSchedules), toUpdate.ToDictionary(
                p => p.Key, p => (Action<ClientTask>)(t => { p.Value(t); updated.Add(t); })), t => t.Id, ct);

        var rolled = RollSchedulesForward(toCreate.Concat(updated).ToList(), dryRun);

        ctx.Result.Created = toCreate.Count;
        ctx.Result.Updated = toUpdate.Count;
        NoteNotInExport(ctx, existing, rows, "ID", "tasks");
        if (unmatchedTechnicians > 0)
            ctx.Result.Notes.Add($"{unmatchedTechnicians} task{(unmatchedTechnicians == 1 ? " names a technician" : "s name technicians")} not found " +
                                 "in FyreApp's Uptick users, so left unassigned. Import Uptick's Users export to assign them.");
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

        var (reports, remarks) = await LinkWaitingRecordsAsync(toCreate, ct);
        if (reports + remarks > 0)
            ctx.Result.Notes.Add($"{reports} report(s) and {remarks} remark(s) imported earlier were linked to their task.");
    }

    // Reports and remarks imported before their task point at it by Uptick task ID; link them now
    private async Task<(int Reports, int Remarks)> LinkWaitingRecordsAsync(IReadOnlyList<ClientTask> created, CancellationToken ct)
    {
        var idByExt = created.Where(t => t.ExternalId != null)
            .GroupBy(t => t.ExternalId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        int reports = 0, remarks = 0;

        foreach (var chunk in idByExt.Keys.Chunk(1000))
        {
            var exts = chunk.ToList();
            foreach (var r in await Db.ServiceReports.Where(r => r.ClientTaskId == null && r.TaskExternalId != null && exts.Contains(r.TaskExternalId)).ToListAsync(ct))
            {
                r.ClientTaskId = idByExt[r.TaskExternalId!];
                reports++;
            }
            foreach (var d in await Db.Defects.Where(d => d.RepairTaskId == null && d.RepairTaskExternalId != null && exts.Contains(d.RepairTaskExternalId)).ToListAsync(ct))
            {
                d.RepairTaskId = idByExt[d.RepairTaskExternalId!];
                remarks++;
            }
        }

        if (reports + remarks > 0)
            await Db.SaveChangesAsync(ct);
        return (reports, remarks);
    }

    // Everything a task takes from its Uptick row, for new and updated tasks alike. The technician is
    // Uptick's when it names a known Uptick user; FyreApp's own fields (routines it was generated from) are left alone.
    private static void Map(ClientTask t, ImportRow row, int siteId, int clientId, IReadOnlyList<MaintenanceSchedule> covered, int? technicianId)
    {
        var status = MapStatus(row.Get("Status"));

        t.Ref = row.Get("Ref");
        t.UptickData = row.ToJson();
        t.PropertyId = siteId;
        t.ClientId = clientId;
        t.Title = Truncate(row.Get("Name") ?? FirstLine(row.Get("Description")) ?? row.Get("Ref") ?? row.Get("ID")!, 120);
        t.Description = BuildDescription(row.Get("Description"), row.Get("Scope of works"));
        t.Status = status;
        t.Priority = MapPriority(row.GetInt("Priority"));
        t.DueDateUtc = row.GetDate("Due");
        t.CreatedUtc = row.GetTimestampUtc("Created") ?? (t.CreatedUtc == default ? DateTime.UtcNow : t.CreatedUtc);
        t.CompletedUtc = row.GetTimestampUtc("Completed Date");
        t.Category = row.Get("Category");
        t.UptickTechnician = Cap(row.Get("Assigned Technician"), 200);
        // Uptick's technician when they're a known Uptick user; otherwise any FyreApp assignment stays
        if (technicianId != null) t.AssignedToUptickUserId = technicianId;
        t.IsActive = row.GetBool("Is Active") ?? status is not (ClientTaskStatus.Completed or ClientTaskStatus.Cancelled);

        foreach (var s in covered.Where(s => !t.CoveredSchedules.Contains(s)))
            t.CoveredSchedules.Add(s);
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
        var site = await Db.Properties.FirstOrDefaultAsync(s => s.IsPlaceholder && s.ExternalId == null && s.Name == NoPropertySiteName, ct);
        if (site != null) return (site.Id, site.ClientId);
        if (dryRun) return (int.MinValue, 0);

        site = new Property { Name = NoPropertySiteName, IsPlaceholder = true, Active = false, Client = await GetUnassignedClientAsync(ct) };
        Db.Properties.Add(site);
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
