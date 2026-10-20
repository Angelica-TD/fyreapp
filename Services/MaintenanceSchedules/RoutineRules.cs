using System.Text.RegularExpressions;
using FyreApp.Models;

namespace FyreApp.Services.MaintenanceSchedules;

// Reading Uptick's routines: their frequency, how far along an occurrence is, and which is next to do
public static class RoutineRules
{
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

    // Uptick's occurrence status codes: "P" pending, "G" task generated (not done yet), "C" complete, "X" cancelled
    public static RoutineStatus OccurrenceStatus(string? status, string? completedDate) =>
        !string.IsNullOrWhiteSpace(completedDate) ? RoutineStatus.Complete
        : (status ?? "").Trim().ToUpperInvariant() switch
        {
            "G" => RoutineStatus.Generated,
            "C" or "COMPLETE" or "COMPLETED" => RoutineStatus.Complete,
            "X" or "CANCELLED" or "CANCELED" => RoutineStatus.Cancelled,
            _ => RoutineStatus.Pending
        };

    // Uptick durations, e.g. "0:30:00" or "1:15:00" -> minutes
    public static int? ParseMinutes(string? input) =>
        TimeSpan.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, out var t) && t > TimeSpan.Zero
            ? (int)Math.Round(t.TotalMinutes)
            : null;
}
