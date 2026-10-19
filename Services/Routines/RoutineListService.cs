using System.Globalization;
using System.Text;
using CsvHelper;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

public interface IRoutineListService
{
    Task<RoutineListVm> SearchAsync(RoutineFilter filter, CancellationToken ct = default);

    // Every routine matching the filters, as CSV (Download button)
    Task<byte[]> DownloadCsvAsync(RoutineFilter filter, CancellationToken ct = default);

    // Turn routines into tasks, as Uptick's "Edit routines → Generate tasks" does.
    // ids = the ticked routines; with allMatching, every routine matching the filter instead.
    Task<GenerateRoutineTasksResult> GenerateTasksAsync(
        IReadOnlyCollection<int> ids, bool allMatching, RoutineFilter filter, string? createdByUserId, CancellationToken ct = default);

    // A property's routines for one year, as on Uptick's property Routines tab
    Task<PropertyRoutinesVm> ForPropertyAsync(int siteId, int year, CancellationToken ct = default);
}

public record GenerateRoutineTasksResult(int TasksCreated, int RoutinesUsed, int RoutinesSkipped);

// Routine occurrences imported from Uptick, listed and filtered like Uptick's Routines page
public class RoutineListService : IRoutineListService
{
    private readonly AppDbContext _db;
    public RoutineListService(AppDbContext db) => _db = db;

    // The one place the filters are applied, so the list, its download and "select all" always agree
    private IQueryable<Routine> Filtered(RoutineFilter filter)
    {
        var q = _db.Routines.AsQueryable();

        if (filter.ClientActive is bool clientActive)
            q = q.Where(o => o.Property.Client.Active == clientActive);

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
            q = q.Where(o => o.Property.Status != null && statuses.Contains(o.Property.Status));
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
                o.Name.ToLower().Contains(term) ||
                o.Property.Name.ToLower().Contains(term) ||
                (o.Property.ExternalId != null && o.Property.ExternalId.ToLower().Contains(term)) ||
                o.Property.Client.Name.ToLower().Contains(term));
        }

        // As Uptick lists them
        return q.OrderBy(o => o.Name).ThenBy(o => o.DueDate).ThenBy(o => o.Property.ExternalId).ThenBy(o => o.Id);
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
                Routine = o.Name,
                PropertyId = o.PropertyId,
                PropertyRef = o.Property.ExternalId ?? o.Property.FyreRef,
                PropertyName = o.Property.Name,
                ClientId = o.Property.ClientId,
                ClientName = o.Property.Client.Name,
                ClientContact = o.Property.Client.PrimaryContactName,
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
                Routine = o.Name,
                PropertyRef = o.Property.ExternalId ?? o.Property.FyreRef,
                Property = o.Property.Name,
                Address = o.Property.AddressDisplay,
                PropertyStatus = o.Property.Status,
                Client = o.Property.Client.Name,
                ClientContact = o.Property.Client.PrimaryContactName,
                o.DueDate,
                o.ToleranceStart,
                o.ToleranceEnd,
                o.Status,
                o.CompletedDate,
                o.ServiceGroup,
                o.DefaultTechnician,
                o.Subcontractor,
                o.EstimatedMinutes,
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
            : _db.Routines.Where(o => ids.Contains(o.Id));

        var occurrences = await selected
            .Include(o => o.Property)
            .Include(o => o.MaintenanceSchedule)
            .Include(o => o.RoutineServiceLevel!).ThenInclude(l => l.RoutineServiceType)
            .ToListAsync(ct);

        // Only routines still waiting for a task; ones already raised or done are left alone
        var pending = occurrences.Where(o => o.Status == RoutineStatus.Pending).ToList();
        var skipped = occurrences.Count - pending.Count;

        // The assets each routine services, listed on the task's Work tab
        var assets = await RoutineAssets.ForAsync(_db, pending
            .Select(o => new RoutineAssets.SiteRoutine(o.PropertyId, o.Name, o.RoutineServiceLevel?.RoutineServiceType.Name ?? RoutineAssets.RoutineType(o.Name)))
            .ToList(), ct);

        // One task per property, service group and month, named like Uptick's ("PM2026/11 Servicing - …")
        var tasks = new List<ClientTask>();
        foreach (var group in pending
                     .GroupBy(o => (o.PropertyId, ServiceGroup: o.ServiceGroup ?? "", o.DueDate.Year, o.DueDate.Month))
                     .OrderBy(g => g.Key.PropertyId).ThenBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            var (siteId, serviceGroup, year, month) = group.Key;
            var site = group.First().Property;
            var routines = group.OrderBy(o => o.Name).ToList();

            var task = new ClientTask
            {
                ClientId = site.ClientId,
                PropertyId = siteId,
                Title = Truncate($"PM{year}/{month:00} {(serviceGroup == "" ? "Routine servicing" : serviceGroup)}", 120),
                Description = Truncate("Scope of works:\n" + string.Join("\n", routines.Select(o => $"- {o.Name}")), 4000),
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

            foreach (var line in routines.Select(o => o.Name).Distinct().SelectMany(r => assets[(siteId, r)]))
                task.Assets.Add(new ServiceTask { AssetId = line.AssetId, Routine = line.Routine });

            foreach (var o in routines)
            {
                o.ClientTask = task;
                o.Status = RoutineStatus.Generated;
            }

            tasks.Add(task);
        }

        _db.ClientTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);

        return new GenerateRoutineTasksResult(tasks.Count, pending.Count, skipped);
    }

    public async Task<PropertyRoutinesVm> ForPropertyAsync(int siteId, int year, CancellationToken ct = default)
    {
        // Due dates are calendar dates stored as UTC midnight
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = yearStart.AddYears(1);
        var today = DateTime.UtcNow.ToSydney().Date;

        var occurrences = await _db.Routines.AsNoTracking()
            .Where(o => o.PropertyId == siteId && o.DueDate >= yearStart && o.DueDate < yearEnd
                        && o.Status != RoutineStatus.Cancelled)
            .Select(o => new
            {
                o.Id,
                Routine = o.Name,
                Type = o.RoutineServiceLevel != null ? o.RoutineServiceLevel.RoutineServiceType.Name : null,
                TypeServiceGroup = o.RoutineServiceLevel != null ? o.RoutineServiceLevel.RoutineServiceType.ServiceGroup : null,
                Level = o.RoutineServiceLevel != null ? o.RoutineServiceLevel.Name : null,
                DisplayCode = o.RoutineServiceLevel != null ? o.RoutineServiceLevel.DisplayCode : null,
                o.ServiceGroup,
                o.DefaultTechnician,
                o.Subcontractor,
                o.EstimatedMinutes,
                o.DueDate,
                o.ToleranceEnd,
                o.CompletedDate,
                o.Status,
                o.ClientTaskId
            })
            .ToListAsync(ct);

        var vm = new PropertyRoutinesVm { PropertyId = siteId, Year = year, Today = today };

        var markers = occurrences.Select(o =>
        {
            var (type, level) = o.Type != null ? (o.Type, o.Level ?? "") : SplitRoutine(o.Routine);
            return new
            {
                Group = o.ServiceGroup ?? o.TypeServiceGroup ?? "Other",
                Technician = o.DefaultTechnician ?? o.Subcontractor,
                o.EstimatedMinutes,
                Type = type,
                Marker = new PropertyRoutineMarkerVm
                {
                    Id = o.Id,
                    Level = level,
                    Code = !string.IsNullOrWhiteSpace(o.DisplayCode) ? o.DisplayCode! : level.Length > 0 ? level[..1].ToUpperInvariant() : "?",
                    DueDate = o.DueDate,
                    CompletedDate = o.CompletedDate,
                    TaskId = o.ClientTaskId,
                    State = MarkerState(o.Status, o.ToleranceEnd, o.CompletedDate, today)
                }
            };
        }).ToList();

        // Groups in the order of their first routine, e.g. "10 - …" before "15 - …", as Uptick lists them
        foreach (var group in markers.GroupBy(m => m.Group).OrderBy(g => g.Min(m => m.Type)))
        {
            var groupVm = new PropertyRoutineGroupVm
            {
                Name = group.Key,
                // The technician most of its routines name
                Technician = group.Where(m => m.Technician != null).GroupBy(m => m.Technician).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
                EstimatedMinutes = group.Sum(m => m.EstimatedMinutes ?? 0)
            };
            foreach (var row in group.GroupBy(m => m.Type).OrderBy(r => r.Key))
            {
                var rowVm = new PropertyRoutineRowVm { Routine = row.Key };
                foreach (var m in row.OrderBy(m => m.Marker.DueDate))
                    rowVm.Months[m.Marker.DueDate.Month - 1].Add(m.Marker);
                groupVm.Rows.Add(rowVm);
            }
            vm.Groups.Add(groupVm);
        }

        // Routine tasks at the property due this year: generated from its routines or schedules, or imported
        // Uptick routine tasks covering its schedules
        var tasks = await _db.ClientTasks.AsNoTracking()
            .Where(t => t.PropertyId == siteId && t.DueDateUtc != null
                        && t.Status != ClientTaskStatus.Cancelled
                        && (t.MaintenanceScheduleId != null || t.CoveredSchedules.Any()
                            || _db.Routines.Any(o => o.ClientTaskId == t.Id)))
            .Select(t => new { t.Id, t.Ref, t.FyreRef, t.Title, t.DueDateUtc, t.Status })
            .ToListAsync(ct);

        foreach (var t in tasks)
        {
            var due = t.DueDateUtc!.Value.ToSydney();
            if (due.Year != year) continue;
            vm.TasksByMonth[due.Month - 1].Add(new PropertyRoutineTaskVm
            {
                Id = t.Id,
                Ref = t.Ref ?? t.FyreRef,
                Title = t.Title,
                DueDate = due.Date,
                Completed = t.Status == ClientTaskStatus.Completed
            });
        }
        foreach (var month in vm.TasksByMonth)
            month.Sort((a, b) => a.DueDate != b.DueDate ? a.DueDate.CompareTo(b.DueDate) : string.CompareOrdinal(a.Title, b.Title));

        return vm;
    }

    // Uptick's legend: forecasted, task created, completed within / outside tolerance, not completed and outside tolerance
    public static RoutineMarkerState MarkerState(RoutineStatus status, DateTime? toleranceEnd, DateTime? completed, DateTime today) =>
        status == RoutineStatus.Complete
            ? (completed is DateTime c && toleranceEnd is DateTime end && c.Date > end.Date
                ? RoutineMarkerState.CompletedOutsideTolerance
                : RoutineMarkerState.CompletedWithinTolerance)
            : toleranceEnd is DateTime e && today > e.Date
                ? RoutineMarkerState.NotCompletedOutsideTolerance
                : status == RoutineStatus.Generated
                    ? RoutineMarkerState.TaskCreated
                    : RoutineMarkerState.Forecasted;

    // "10 - Portable and Wheeled Fire Extinguishers: Six-monthly" → (routine, level)
    private static (string Type, string Level) SplitRoutine(string routine)
    {
        var i = routine.LastIndexOf(": ", StringComparison.Ordinal);
        return i < 0 ? (routine, "") : (routine[..i], routine[(i + 2)..]);
    }

    public static string StatusLabel(RoutineStatus s) => s switch
    {
        RoutineStatus.Generated => "Task raised",
        _ => s.ToString()
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
