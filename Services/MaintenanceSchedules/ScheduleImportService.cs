using System.Globalization;
using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.ViewModels.MaintenanceSchedules;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.MaintenanceSchedules;

// Uptick exports one row per routine *occurrence* (e.g. "10 - Portable ...: Six-monthly" due 2026-11-01).
// Rows are grouped into one site-level schedule per (property, interval), using the earliest
// outstanding due date as NextRunDate. Sites that already have a schedule for that interval are skipped.
public class ScheduleImportService : IScheduleImportService
{
    private const int MaxIssues = 500;
    private const int MaxRowsPerIssue = 200;

    private readonly AppDbContext _db;
    public ScheduleImportService(AppDbContext db) => _db = db;

    private sealed record SiteRef(int Id, string Name, string? ExternalId, string ClientName);

    private sealed class Group
    {
        public required SiteRef Site { get; init; }
        public required int Months { get; init; }
        public required string FrequencyLabel { get; init; }
        public string? PropertyRef { get; set; }
        public List<(string Routine, DateTime Due, bool Done, bool TaskRaised)> Occurrences { get; } = new();
        public SortedSet<string> Routines { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<RoutineServiceLevel> Levels { get; } = new();
    }

    public async Task<ScheduleImportResultVm> ImportUptickAsync(
        Stream stream, string fileName, bool dryRun, CancellationToken ct = default)
    {
        var result = new ScheduleImportResultVm { DryRun = dryRun, FileName = fileName };

        if (!TabularFileReader.IsSupported(fileName))
        {
            result.Error = $"Unsupported file type: {Path.GetExtension(fileName)}. Upload CSV or XLSX.";
            return result;
        }

        List<Dictionary<string, string?>> rows;
        try
        {
            rows = await TabularFileReader.ReadAsync(stream, fileName, ct);
        }
        catch (Exception ex)
        {
            result.Error = $"Could not read file: {ex.Message}";
            return result;
        }

        var issues = new Dictionary<string, ScheduleImportIssueVm>(StringComparer.OrdinalIgnoreCase);
        void AddIssue(string type, string key, int row, string message)
        {
            var k = $"{type}::{key}";
            if (!issues.TryGetValue(k, out var issue))
            {
                if (issues.Count >= MaxIssues) return;
                issues[k] = issue = new ScheduleImportIssueVm { Type = type, Key = key, Message = message };
            }
            if (issue.Rows.Count < MaxRowsPerIssue) issue.Rows.Add(row);
        }

        // Uptick routine levels (reference data): "<routine service type>: <level>" -> level
        var levelsByRoutine = (await _db.RoutineServiceLevels.Include(l => l.RoutineServiceType).ToListAsync(ct))
            .GroupBy(l => $"{l.RoutineServiceType.Name.Trim()}: {l.Name.Trim()}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Site lookups
        var sites = await _db.Sites.AsNoTracking()
            .Select(s => new SiteRef(s.Id, s.Name, s.ExternalId, s.Client.Name))
            .ToListAsync(ct);

        var sitesByRef = sites
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalId))
            .ToDictionary(s => s.ExternalId!.Trim(), StringComparer.OrdinalIgnoreCase);

        var sitesByName = sites
            .GroupBy(s => NameKey(s.ClientName, s.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // SiteId -> Property ref to be written to Site.ExternalId
        var linkRefs = new Dictionary<int, string>();
        var groups = new Dictionary<(int SiteId, int Months), Group>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;

            if (row.Values.All(string.IsNullOrWhiteSpace)) continue;
            result.TotalRows++;

            string? Get(string header)
            {
                var nk = TabularFileReader.NormalizeHeader(header);
                var match = row.Keys.FirstOrDefault(h => TabularFileReader.NormalizeHeader(h) == nk);
                var val = match == null ? null : row[match];
                return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }

            var routine = Get("Routine");
            var dueText = Get("Due Date");
            var propertyRef = Get("Property ref");
            var propertyName = Get("Property name");
            var clientName = Get("Client name");

            if (routine == null || dueText == null)
            {
                result.SkippedInvalid++;
                AddIssue("MissingRequired", "(blank)", rowNumber, "Routine and Due Date are required. Row skipped.");
                continue;
            }

            var due = ParseDate(dueText);
            if (due == null)
            {
                result.SkippedInvalid++;
                AddIssue("InvalidDueDate", dueText, rowNumber, $"Could not read Due Date '{dueText}'. Row skipped.");
                continue;
            }

            // The frequency in the name wins (e.g. "Annual Evacuation Drill", configured as 1 month in Uptick);
            // otherwise use the interval configured on the Uptick level
            var level = levelsByRoutine.GetValueOrDefault(routine.Trim());
            var (months, frequencyLabel) = ParseFrequency(routine);
            months ??= level?.IntervalMonths > 0 ? level.IntervalMonths : null;
            if (months == null)
            {
                result.SkippedUnsupportedFrequency++;
                AddIssue("UnsupportedFrequency", frequencyLabel, rowNumber,
                    $"Frequency '{frequencyLabel}' can't be represented as a whole number of months. Row skipped.");
                continue;
            }

            // Resolve the site: Property ref first, then client name + property name
            SiteRef? site = null;
            if (propertyRef != null && sitesByRef.TryGetValue(propertyRef, out var byRef))
            {
                site = byRef;
            }
            else if (propertyName != null && clientName != null &&
                     sitesByName.TryGetValue(NameKey(clientName, propertyName), out var byName))
            {
                if (byName.Count > 1)
                {
                    result.SkippedSiteNotFound++;
                    AddIssue("AmbiguousSite", $"{clientName} / {propertyName}", rowNumber,
                        "More than one property matches this client and property name. Set the property's External ID to disambiguate. Row skipped.");
                    continue;
                }

                var candidate = byName[0];
                if (propertyRef != null && !string.IsNullOrWhiteSpace(candidate.ExternalId))
                {
                    // Name matches but the site is already linked to a different Uptick property
                    result.SkippedSiteNotFound++;
                    AddIssue("PropertyRefMismatch", propertyRef, rowNumber,
                        $"Property '{propertyName}' is linked to '{candidate.ExternalId}', not '{propertyRef}'. Row skipped.");
                    continue;
                }

                if (propertyRef != null)
                {
                    if (linkRefs.TryGetValue(candidate.Id, out var pending) &&
                        !string.Equals(pending, propertyRef, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SkippedSiteNotFound++;
                        AddIssue("PropertyRefMismatch", propertyRef, rowNumber,
                            $"Property '{propertyName}' matches rows with both '{pending}' and '{propertyRef}'. Row skipped.");
                        continue;
                    }
                    linkRefs[candidate.Id] = propertyRef;
                }

                site = candidate;
            }

            if (site == null)
            {
                result.SkippedSiteNotFound++;
                AddIssue("SiteNotFound", propertyRef ?? $"{clientName} / {propertyName}", rowNumber,
                    $"No property found for '{propertyName}' ({clientName}). Row skipped.");
                continue;
            }

            var key = (site.Id, months.Value);
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = new Group
                {
                    Site = site,
                    Months = months.Value,
                    FrequencyLabel = frequencyLabel,
                    PropertyRef = site.ExternalId ?? propertyRef
                };
            }

            var done = IsDone(Get("Status"), Get("Completed Date"));
            group.Occurrences.Add((routine, due.Value, done, !done && IsTaskRaised(Get("Status"))));
            group.Routines.Add(routine);
            if (level != null) group.Levels.Add(level);
        }

        // Existing schedules and intervals
        var groupSiteIds = groups.Keys.Select(k => k.SiteId).Distinct().ToList();
        // Tracked, so re-running the import can link existing schedules to their routines
        var existing = (await _db.MaintenanceSchedules
                .Include(s => s.MaintenanceInterval)
                .Include(s => s.RoutineLevels)
                .Where(s => s.TargetType == ScheduleTargetType.Site && s.SiteId != null && groupSiteIds.Contains(s.SiteId.Value))
                .ToListAsync(ct))
            .GroupBy(s => (s.SiteId!.Value, s.MaintenanceInterval.Months))
            .ToDictionary(g => g.Key, g => g.First());
        var routineLinksAdded = 0;

        var intervals = await _db.MaintenanceIntervals.ToListAsync(ct);
        var newIntervals = new Dictionary<int, MaintenanceInterval>();

        MaintenanceInterval IntervalFor(int months, string label)
        {
            var match = intervals.Where(i => i.Months == months).OrderBy(i => i.Id).FirstOrDefault();
            if (match != null) return match;
            if (newIntervals.TryGetValue(months, out var pending)) return pending;

            var name = intervals.Any(i => string.Equals(i.Name, label, StringComparison.OrdinalIgnoreCase))
                ? $"{label} ({months} months)"
                : label;

            var created = new MaintenanceInterval { Name = name, Months = months };
            newIntervals[months] = created;
            return created;
        }

        var toCreate = new List<MaintenanceSchedule>();

        foreach (var group in groups.Values
                     .OrderBy(g => g.Site.ClientName)
                     .ThenBy(g => g.Site.Name)
                     .ThenBy(g => g.Months))
        {
            // Next outstanding occurrence; if everything is done, roll forward from the latest one
            var next = NextOutstanding(group.Occurrences, DateTime.UtcNow)
                       ?? group.Occurrences.Max(o => o.Due).AddMonths(group.Months);

            var existingSchedule = existing.GetValueOrDefault((group.Site.Id, group.Months));
            var isExisting = existingSchedule != null;
            var interval = isExisting
                ? existingSchedule!.MaintenanceInterval
                : IntervalFor(group.Months, group.FrequencyLabel);

            result.Items.Add(new ScheduleImportItemVm
            {
                ClientName = group.Site.ClientName,
                SiteName = group.Site.Name,
                PropertyRef = group.PropertyRef,
                IntervalName = interval.Name,
                NextRunDate = next,
                Routines = group.Routines.ToList(),
                WillCreate = !isExisting
            });

            if (isExisting)
            {
                // Existing schedules aren't changed, but pick up routines they don't list yet
                var missing = group.Levels.Where(l => !existingSchedule!.RoutineLevels.Contains(l)).ToList();
                routineLinksAdded += missing.Count;
                if (!dryRun)
                    foreach (var l in missing) existingSchedule!.RoutineLevels.Add(l);

                result.SkippedExisting++;
                continue;
            }

            toCreate.Add(new MaintenanceSchedule
            {
                TargetType = ScheduleTargetType.Site,
                SiteId = group.Site.Id,
                MaintenanceInterval = interval,
                StartDate = next.AddMonths(-group.Months),
                NextRunDate = next,
                IsActive = true,
                RoutineLevels = group.Levels.ToList()
            });
        }

        result.Created = toCreate.Count;
        result.SitesLinked = linkRefs.Count;
        result.RoutineLinksAdded = routineLinksAdded;
        result.NewIntervals = newIntervals.Values.Select(i => $"{i.Name} ({i.Months} months)").ToList();
        result.Issues = issues.Values.OrderBy(i => i.Type).ThenBy(i => i.Key).ToList();

        if (dryRun || (toCreate.Count == 0 && linkRefs.Count == 0 && routineLinksAdded == 0))
            return result;

        try
        {
            if (linkRefs.Count > 0)
            {
                var linkIds = linkRefs.Keys.ToList();
                var sitesToLink = await _db.Sites.Where(s => linkIds.Contains(s.Id)).ToListAsync(ct);
                foreach (var s in sitesToLink)
                    s.ExternalId = linkRefs[s.Id];
            }

            _db.MaintenanceSchedules.AddRange(toCreate);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            result.Created = 0;
            result.SitesLinked = 0;
            result.Error = "Database rejected the import; nothing was saved. " + (ex.InnerException?.Message ?? ex.Message);
        }

        return result;
    }

    // Earliest occurrence still to do. Worked out per routine (a schedule merges every routine at the property
    // with the same interval). Unfiltered exports keep old "G" (task raised) rows that Uptick never moved on,
    // even when their task was completed, so an open occurrence is ignored when a later, already-due one was
    // completed or had its task raised: work has moved past it. Only already-due ones count, so work done early
    // (e.g. a 2031 occurrence marked complete) doesn't hide earlier pending ones.
    public static DateTime? NextOutstanding(
        IEnumerable<(string Routine, DateTime Due, bool Done, bool TaskRaised)> occurrences, DateTime today) =>
        occurrences
            .GroupBy(o => o.Routine.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var lastDone = r.Where(o => o.Done && o.Due <= today).Select(o => (DateTime?)o.Due).Max();
                var lastRaised = r.Where(o => o.TaskRaised && o.Due <= today).Select(o => (DateTime?)o.Due).Max();
                return r.Where(o => !o.Done &&
                                    (lastDone == null || o.Due > lastDone) &&
                                    (lastRaised == null || o.Due >= lastRaised))
                    .Select(o => (DateTime?)o.Due)
                    .Min();
            })
            .Min();

    // "10 - Portable and Wheeled Fire Extinguishers: Five-yearly" -> (60, "Five-yearly")
    public static (int? Months, string Label) ParseFrequency(string routine)
    {
        var colon = routine.LastIndexOf(':');
        var label = (colon >= 0 ? routine[(colon + 1)..] : routine).Trim();
        var months = MonthsFor(label);

        // Custom levels are named rather than "<routine>: <frequency>", e.g. "Annual Evacuation Drill";
        // use the frequency word in the name when there's exactly one
        if (months == null)
        {
            var words = Regex.Matches(label, "[A-Za-z0-9]+").Select(m => m.Value).ToList();
            var found = new HashSet<int>();
            var inPair = new HashSet<int>();

            // Two-word frequencies first ("Six monthly"), so "monthly" on its own isn't also counted
            for (var i = 0; i + 1 < words.Count; i++)
            {
                if (MonthsFor($"{words[i]}-{words[i + 1]}") is int pair)
                {
                    found.Add(pair);
                    inPair.Add(i);
                    inPair.Add(i + 1);
                }
            }

            for (var i = 0; i < words.Count; i++)
            {
                if (!inPair.Contains(i) && MonthsFor(words[i]) is int single)
                    found.Add(single);
            }

            if (found.Count == 1) months = found.Single();
        }

        return (months, label);
    }

    private static int? MonthsFor(string label)
    {
        var key = Regex.Replace(label.Trim().ToLowerInvariant(), @"[\s_]+", "-");

        int? months = key switch
        {
            "monthly" => 1,
            "bi-monthly" or "bimonthly" => 2,
            "quarterly" => 3,
            "half-yearly" or "semi-annual" or "semi-annually" or "biannual" or "biannually" => 6,
            "yearly" or "annual" or "annually" => 12,
            "biennial" or "biennially" => 24,
            _ => null
        };

        if (months == null)
        {
            var m = Regex.Match(key, @"^(\d+|[a-z-]+?)-(monthly|yearly)$");
            if (m.Success)
            {
                var n = int.TryParse(m.Groups[1].Value, out var digits)
                    ? digits
                    : NumberWord(m.Groups[1].Value.Replace("-", ""));
                if (n > 0)
                    months = m.Groups[2].Value == "yearly" ? n * 12 : n;
            }
        }

        return months;
    }

    private static int NumberWord(string word) => word switch
    {
        "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
        "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
        "eleven" => 11, "twelve" => 12, "fifteen" => 15, "eighteen" => 18,
        "twenty" => 20, "twentyfive" => 25, "thirty" => 30,
        _ => 0
    };

    // Uptick status codes: "P" pending, "G" task generated (not done yet), "C" complete. Treat completed/cancelled as done.
    private static bool IsDone(string? status, string? completedDate)
    {
        if (!string.IsNullOrWhiteSpace(completedDate)) return true;
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s is "c" or "x" or "completed" or "complete" or "cancelled" or "canceled";
    }

    private static bool IsTaskRaised(string? status) =>
        (status ?? "").Trim().Equals("G", StringComparison.OrdinalIgnoreCase);

    private static string NameKey(string clientName, string siteName) =>
        $"{TabularFileReader.NormalizeHeader(clientName)}|{TabularFileReader.NormalizeHeader(siteName)}";

    private static DateTime? ParseDate(string input)
    {
        var formats = new[]
        {
            "yyyy-MM-dd", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd HH:mm:ss",
            "d/M/yyyy", "dd/MM/yyyy", "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm"
        };

        if (DateTime.TryParseExact(input.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);

        return null;
    }
}
