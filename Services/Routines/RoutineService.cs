using System.Globalization;
using System.Text;
using CsvHelper;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

public interface IRoutineService
{
    Task<RoutineListVm> SearchAsync(RoutineFilter filter, CancellationToken ct = default);

    // Every routine matching the filters, as CSV (Download button)
    Task<byte[]> DownloadCsvAsync(RoutineFilter filter, CancellationToken ct = default);

    // Turn routines into tasks, as Uptick's "Edit routines → Generate tasks" does.
    // ids = the ticked routines; with allMatching, every routine matching the filter instead.
    Task<GenerateRoutineTasksResult> GenerateTasksAsync(
        IReadOnlyCollection<int> ids, bool allMatching, RoutineFilter filter, string? createdByUserId, CancellationToken ct = default);
}

public record GenerateRoutineTasksResult(int TasksCreated, int RoutinesUsed, int RoutinesSkipped);

// Routine occurrences imported from Uptick, listed and filtered like Uptick's Routines page
public class RoutineService : IRoutineService
{
    private readonly AppDbContext _db;
    public RoutineService(AppDbContext db) => _db = db;

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<RoutineOccurrence> Filtered(RoutineFilter filter)
    {
        var q = _db.RoutineOccurrences.AsQueryable();

        if (filter.ClientActive is bool clientActive)
            q = q.Where(o => o.Site.Client.Active == clientActive);

        // Due dates are calendar dates stored as UTC midnight
        if (filter.DueFrom is DateTime from)
        {
            var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            q = q.Where(o => o.DueDate >= fromUtc);
        }
        if (filter.DueTo is DateTime to)
        {
            var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            q = q.Where(o => o.DueDate < toUtc);
        }

        if (filter.PropertyStatus.Count > 0)
        {
            var statuses = filter.PropertyStatus.Select(s => s.ToUpperInvariant()).ToList();
            q = q.Where(o => o.Site.Status != null && statuses.Contains(o.Site.Status));
        }

        if (filter.Status.Count > 0)
        {
            var statuses = filter.Status.ToList();
            q = q.Where(o => statuses.Contains(o.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(o =>
                o.Routine.ToLower().Contains(term) ||
                o.Site.Name.ToLower().Contains(term) ||
                (o.Site.ExternalId != null && o.Site.ExternalId.ToLower().Contains(term)) ||
                o.Site.Client.Name.ToLower().Contains(term));
        }

        // As Uptick lists them
        return q.OrderBy(o => o.Routine).ThenBy(o => o.DueDate).ThenBy(o => o.Site.ExternalId).ThenBy(o => o.Id);
    }

    public async Task<RoutineListVm> SearchAsync(RoutineFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter).AsNoTracking();

        var total = await q.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)RoutineListVm.PageSize)));
        filter.Page = page;

        var items = await q
            .Skip((page - 1) * RoutineListVm.PageSize)
            .Take(RoutineListVm.PageSize)
            .Select(o => new RoutineListItemVm
            {
                Id = o.Id,
                Routine = o.Routine,
                SiteId = o.SiteId,
                PropertyRef = o.Site.ExternalId ?? o.Site.FyreRef,
                PropertyName = o.Site.Name,
                ClientId = o.Site.ClientId,
                ClientName = o.Site.Client.Name,
                ClientContact = o.Site.Client.PrimaryContactName,
                DueDate = o.DueDate,
                Status = o.Status,
                MaintenanceScheduleId = o.MaintenanceScheduleId,
                ClientTaskId = o.ClientTaskId
            })
            .ToListAsync(ct);

        return new RoutineListVm { Filter = filter, Items = items, Total = total };
    }

    public async Task<byte[]> DownloadCsvAsync(RoutineFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).AsNoTracking()
            .Select(o => new
            {
                o.Routine,
                PropertyRef = o.Site.ExternalId ?? o.Site.FyreRef,
                Property = o.Site.Name,
                Address = o.Site.AddressDisplay,
                PropertyStatus = o.Site.Status,
                Client = o.Site.Client.Name,
                ClientContact = o.Site.Client.PrimaryContactName,
                o.DueDate,
                o.ToleranceStart,
                o.ToleranceEnd,
                o.Status,
                o.CompletedDate,
                o.ServiceGroup,
                TaskRef = o.ClientTask != null ? o.ClientTask.Ref ?? o.ClientTask.FyreRef : null
            })
            .ToListAsync(ct);

        await using var ms = new MemoryStream();
        // UTF-8 with BOM so Excel shows names correctly
        await using (var writer = new StreamWriter(ms, new UTF8Encoding(true)))
        await using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var h in new[] { "Routine", "Property ref", "Property", "Address", "Property status", "Client", "Client contact",
                                       "Due", "Tolerance start", "Tolerance end", "Status", "Completed", "Service group", "Task" })
                csv.WriteField(h);
            await csv.NextRecordAsync();

            foreach (var r in rows)
            {
                csv.WriteField(r.Routine);
                csv.WriteField(r.PropertyRef);
                csv.WriteField(r.Property);
                csv.WriteField(r.Address);
                csv.WriteField(r.PropertyStatus);
                csv.WriteField(r.Client);
                csv.WriteField(r.ClientContact);
                csv.WriteField(r.DueDate.ToString("yyyy-MM-dd"));
                csv.WriteField(r.ToleranceStart?.ToString("yyyy-MM-dd"));
                csv.WriteField(r.ToleranceEnd?.ToString("yyyy-MM-dd"));
                csv.WriteField(StatusLabel(r.Status));
                csv.WriteField(r.CompletedDate?.ToString("yyyy-MM-dd"));
                csv.WriteField(r.ServiceGroup);
                csv.WriteField(r.TaskRef);
                await csv.NextRecordAsync();
            }
        }

        return ms.ToArray();
    }

    public async Task<GenerateRoutineTasksResult> GenerateTasksAsync(
        IReadOnlyCollection<int> ids, bool allMatching, RoutineFilter filter, string? createdByUserId, CancellationToken ct = default)
    {
        var selected = allMatching
            ? Filtered(filter)
            : _db.RoutineOccurrences.Where(o => ids.Contains(o.Id));

        var occurrences = await selected
            .Include(o => o.Site)
            .Include(o => o.MaintenanceSchedule)
            .ToListAsync(ct);

        // Only routines still waiting for a task; ones already raised or done are left alone
        var pending = occurrences.Where(o => o.Status == RoutineOccurrenceStatus.Pending).ToList();
        var skipped = occurrences.Count - pending.Count;

        // One task per property, service group and month, named like Uptick's ("PM2026/11 Servicing - …")
        var tasks = new List<ClientTask>();
        foreach (var group in pending
                     .GroupBy(o => (o.SiteId, ServiceGroup: o.ServiceGroup ?? "", o.DueDate.Year, o.DueDate.Month))
                     .OrderBy(g => g.Key.SiteId).ThenBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            var (siteId, serviceGroup, year, month) = group.Key;
            var site = group.First().Site;
            var routines = group.OrderBy(o => o.Routine).ToList();

            var task = new ClientTask
            {
                ClientId = site.ClientId,
                SiteId = siteId,
                Title = Truncate($"PM{year}/{month:00} {(serviceGroup == "" ? "Routine servicing" : serviceGroup)}", 120),
                Description = Truncate("Scope of works:\n" + string.Join("\n", routines.Select(o => $"- {o.Routine}")), 4000),
                Category = "I&T",
                Status = ClientTaskStatus.Open,
                Priority = ClientTaskPriority.Normal,
                // Uptick's routine tasks are due at the end of the month
                DueDateUtc = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc),
                CreatedUtc = DateTime.UtcNow,
                CreatedByUserId = createdByUserId
            };

            foreach (var schedule in routines.Select(o => o.MaintenanceSchedule).OfType<MaintenanceSchedule>().Distinct())
                task.CoveredSchedules.Add(schedule);

            foreach (var o in routines)
            {
                o.ClientTask = task;
                o.Status = RoutineOccurrenceStatus.Generated;
            }

            tasks.Add(task);
        }

        _db.ClientTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);

        return new GenerateRoutineTasksResult(tasks.Count, pending.Count, skipped);
    }

    public static string StatusLabel(RoutineOccurrenceStatus s) => s switch
    {
        RoutineOccurrenceStatus.Generated => "Task raised",
        _ => s.ToString()
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
