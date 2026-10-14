using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

public interface IRoutineSetupService
{
    // The "Add routine services" modal for a property
    Task<AddRoutineServicesVm> FormAsync(int siteId, CancellationToken ct = default);

    // Adds a routine service per routine type with ticked levels, and forecasts their occurrences
    Task<AddRoutineServicesResult> AddAsync(int siteId, AddRoutineServicesInput input, string? author, CancellationToken ct = default);
}

// FirstDue: the earliest routine forecast, so the Routines tab can open on its year
public record AddRoutineServicesResult(int Services, int Occurrences, string? Error = null, DateTime? FirstDue = null);

// Routines set up on a property in FyreApp, as Uptick's "Add routine services" does
public class RoutineSetupService : IRoutineSetupService
{
    // How far ahead occurrences are forecast (Uptick's routines export runs about ten years out)
    public const int ForecastYears = 10;

    private readonly AppDbContext _db;
    public RoutineSetupService(AppDbContext db) => _db = db;

    public async Task<AddRoutineServicesVm> FormAsync(int siteId, CancellationToken ct = default)
    {
        var types = await _db.RoutineServiceTypes.AsNoTracking()
            .Where(t => t.Active)
            .Include(t => t.Levels.Where(l => l.Active))
            .ToListAsync(ct);

        // Routines the property already has, per type: its levels with occurrences, imported or set up here
        var existing = (await _db.RoutineOccurrences.AsNoTracking()
                .Where(o => o.SiteId == siteId && o.Status != RoutineOccurrenceStatus.Cancelled && o.RoutineServiceLevelId != null)
                .Select(o => new { o.RoutineServiceLevel!.RoutineServiceTypeId, o.RoutineServiceLevelId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(o => o.RoutineServiceTypeId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Applicable assets: active assets at the property whose asset type lists the routine
        // (Uptick asset types export "Applicable Routines", one routine name per line)
        var assets = await _db.Assets.AsNoTracking()
            .Where(a => a.SiteId == siteId && a.IsActive)
            .Select(a => new { a.Id, Types = a.AssetTypes.Select(t => t.UptickData).ToList() })
            .ToListAsync(ct);
        var applicable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var routines = asset.Types.SelectMany(ApplicableRoutines).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var r in routines) applicable[r] = applicable.GetValueOrDefault(r) + 1;
        }

        return new AddRoutineServicesVm
        {
            SiteId = siteId,
            Types = types
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new RoutineTypeOptionVm
                {
                    Id = t.Id,
                    Name = t.Name,
                    Reference = t.StandardReference ?? t.Standard,
                    Standard = t.Standard,
                    Levels = t.Levels
                        .OrderBy(l => l.IntervalMonths).ThenBy(l => l.Name)
                        .Select(l => new RoutineLevelOptionVm { Id = l.Id, Name = l.Name, DefaultEnabled = l.DefaultEnabled })
                        .ToList(),
                    ApplicableAssets = applicable.GetValueOrDefault(t.Name),
                    ExistingRoutines = existing.GetValueOrDefault(t.Id)
                })
                .ToList(),
            Standards = types.Select(t => t.Standard).OfType<string>().Where(s => s != "").Distinct().Order().ToList(),
            Contractors = await _db.Contractors.AsNoTracking().Where(c => c.Active).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(ct)
        };
    }

    private static IEnumerable<string> ApplicableRoutines(string? assetTypeUptickData) =>
        UptickJson.Parse(assetTypeUptickData)
            .Where(f => f.Header == "Applicable Routines")
            .SelectMany(f => f.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public async Task<AddRoutineServicesResult> AddAsync(int siteId, AddRoutineServicesInput input, string? author, CancellationToken ct = default)
    {
        if (input.StartDate is not DateTime start || input.LastAnnualDate is not DateTime lastAnnual)
            return new AddRoutineServicesResult(0, 0, "Start date and last annual date are required.");
        if (input.EndDate is DateTime endDate && endDate.Date < start.Date)
            return new AddRoutineServicesResult(0, 0, "The end date is before the start date.");
        if (input.Levels.Count == 0)
            return new AddRoutineServicesResult(0, 0, "Tick at least one routine type and level.");
        if (!await _db.Sites.AnyAsync(s => s.Id == siteId, ct))
            return new AddRoutineServicesResult(0, 0, "Property not found.");

        var levelIds = input.Levels.Distinct().ToList();
        var levels = await _db.RoutineServiceLevels
            .Include(l => l.RoutineServiceType)
            .Where(l => levelIds.Contains(l.Id) && l.Active && l.RoutineServiceType.Active)
            .ToListAsync(ct);

        var today = DateTime.UtcNow.ToSydney().Date;
        var subcontractor = string.IsNullOrWhiteSpace(input.Subcontractor) ? null : input.Subcontractor.Trim();
        var services = new List<PropertyRoutineService>();
        var occurrences = 0;

        foreach (var group in levels.GroupBy(l => l.RoutineServiceType).OrderBy(g => g.Key.Name))
        {
            var type = group.Key;
            var service = new PropertyRoutineService
            {
                SiteId = siteId,
                RoutineServiceTypeId = type.Id,
                StartDate = Utc(start),
                LastAnnualDate = Utc(lastAnnual),
                EndDate = input.EndDate is DateTime e ? Utc(e) : null,
                Subcontractor = subcontractor,
                CreatedBy = author
            };
            foreach (var l in group) service.Levels.Add(l);

            var byId = group.ToDictionary(l => l.Id);
            foreach (var due in Forecast(group.Select(ForecastLevel.From), service.StartDate, service.LastAnnualDate, service.EndDate, today))
            {
                var level = byId[due.Level.Id];
                service.Occurrences.Add(new RoutineOccurrence
                {
                    SiteId = siteId,
                    Routine = Cap($"{type.Name}: {level.Name}", 300),
                    RoutineServiceLevelId = level.Id,
                    DueDate = due.DueDate,
                    ToleranceStart = due.ToleranceStart,
                    ToleranceEnd = due.ToleranceEnd,
                    Status = RoutineOccurrenceStatus.Pending,
                    ServiceGroup = Cap(type.ServiceGroup, 100),
                    Subcontractor = subcontractor
                });
                occurrences++;
            }
            services.Add(service);
        }

        if (services.Count == 0)
            return new AddRoutineServicesResult(0, 0, "None of the ticked levels are active routine levels.");

        _db.PropertyRoutineServices.AddRange(services);
        await _db.SaveChangesAsync(ct);
        var firstDue = services.SelectMany(s => s.Occurrences).Select(o => (DateTime?)o.DueDate).Min();
        return new AddRoutineServicesResult(services.Count, occurrences, FirstDue: firstDue);
    }

    // ---- Forecasting

    public sealed record ForecastLevel(int Id, int IntervalMonths, int? OffsetMonths, int? Rank, bool SupersedesLowerRank,
        int? ToleranceInterval, string? ToleranceUnit)
    {
        public static ForecastLevel From(RoutineServiceLevel l) =>
            new(l.Id, l.IntervalMonths, l.OffsetMonths, l.ServiceRank, l.SupersedesLowerRank, l.ToleranceInterval, l.ToleranceUnit);

        // Higher rank first, then the longer interval (an annual outranks a six-monthly)
        public (int, int) Order => (Rank ?? 0, IntervalMonths);
    }

    public sealed record ForecastOccurrence(ForecastLevel Level, DateTime DueDate, DateTime? ToleranceStart, DateTime? ToleranceEnd);

    // When each level falls due, as Uptick forecasts routines: counting from the last annual service, each level
    // is first due its offset (else its interval) later, then every interval; due on the 1st of the month, from the
    // start date up to the end date (or ForecastYears ahead). In a month where a level that supersedes lower ranks
    // is due, the lower-ranked levels aren't (the annual service covers the six-monthly one).
    public static IReadOnlyList<ForecastOccurrence> Forecast(IEnumerable<ForecastLevel> levels, DateTime start, DateTime lastAnnual,
        DateTime? end, DateTime today)
    {
        var anchor = FirstOfMonth(lastAnnual);
        var from = FirstOfMonth(start);
        var horizon = FirstOfMonth(today).AddYears(ForecastYears);
        if (end is DateTime e && e < horizon) horizon = e;

        var all = new List<ForecastOccurrence>();
        foreach (var level in levels.Where(l => l.IntervalMonths > 0))
        {
            for (var months = level.OffsetMonths is int offset && offset > 0 ? offset : level.IntervalMonths;
                 anchor.AddMonths(months) <= horizon;
                 months += level.IntervalMonths)
            {
                var due = anchor.AddMonths(months);
                if (due < from) continue;
                var (tolStart, tolEnd) = Tolerance(due, level.ToleranceInterval, level.ToleranceUnit);
                all.Add(new ForecastOccurrence(level, due, tolStart, tolEnd));
            }
        }

        return all
            .GroupBy(o => o.DueDate)
            .SelectMany(month =>
            {
                var superseding = month.Where(o => o.Level.SupersedesLowerRank).Select(o => o.Level.Order).DefaultIfEmpty().Max();
                return month.Where(o => o.Level.Order.CompareTo(superseding) >= 0);
            })
            .OrderBy(o => o.DueDate).ThenBy(o => o.Level.IntervalMonths)
            .ToList();
    }

    // How early / late it can be done, e.g. 2 M either side of the due date; none when the level has no tolerance
    private static (DateTime?, DateTime?) Tolerance(DateTime due, int? interval, string? unit)
    {
        if (interval is not int n || n <= 0) return (null, null);
        return unit?.ToUpperInvariant() switch
        {
            "D" => (due.AddDays(-n), due.AddDays(n)),
            "W" => (due.AddDays(-7 * n), due.AddDays(7 * n)),
            "Y" => (due.AddYears(-n), due.AddYears(n)),
            _ => (due.AddMonths(-n), due.AddMonths(n))
        };
    }

    // Calendar dates are kept as UTC midnight
    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
    private static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string? Cap(string? s, int max) => s == null || s.Length <= max ? s : s[..(max - 1)] + "…";
}
