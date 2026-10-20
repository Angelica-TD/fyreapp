using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Imports;

namespace FyreApp.ViewModels.Tasks;

// The task page, laid out like Uptick's: Task tab (details, process and notes, schedule and access) and
// Work tab (the asset repairs it does)
public class TaskPageVm
{
    public static readonly (string Key, string Label)[] Tabs = [("task", "Task"), ("work", "Work")];

    public ClientTask Task { get; set; } = null!;
    public string Tab { get; set; } = "task";

    // Reports done on this task, remarks it repairs, and routines it was generated from
    public IReadOnlyList<Report> Reports { get; set; } = [];
    public IReadOnlyList<Remark> Repairs { get; set; } = [];
    public IReadOnlyList<string> Routines { get; set; } = [];

    // The routines its Work tab lists assets for: the ones it was generated from, or for imported Uptick tasks
    // the ones its scope of works names
    public IReadOnlyList<string> ServiceRoutines { get; set; } = [];

    // The assets it services for those routines (Work tab), by routine
    public IReadOnlyList<(string Routine, Asset Asset)> ServiceAssets { get; set; } = [];

    // Its routines (Work tab's Asset Maintenance summary), each with its routine record (ID, due date) when found
    public IReadOnlyList<(string Routine, Routine? Occurrence)> Visits { get; set; } = [];

    // Remarks identified or resolved on this task (Work tab's Remark registry), and each serviced asset's open remarks
    public IReadOnlyList<(Remark Remark, bool Resolved)> Remarks { get; set; } = [];
    public IReadOnlyDictionary<int, int> OpenRemarks { get; set; } = new Dictionary<int, int>();

    // The assets serviced on this task, with the date (their last service, claimed by this task: see TaskPageService)
    public IReadOnlyDictionary<int, DateTime> PerformedOn { get; set; } = new Dictionary<int, DateTime>();

    public DateTime? Performed(Asset a) => PerformedOn.TryGetValue(a.Id, out var d) ? d : null;

    public DateTime WorkFrom => WorkWindow(Task).From;
    public DateTime? WorkTo => WorkWindow(Task).To;

    // When a task's work is done: from its due month (or scheduled date, if earlier) to when it was performed or
    // completed (open-ended until then)
    public static (DateTime From, DateTime? To) WorkWindow(ClientTask t)
    {
        DateTime? UDate(string header) =>
            UptickJson.Value(t.UptickData, header) is { } s
            && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d.Date : null;

        var from = t.DueDateUtc is DateTime due ? new DateTime(due.Year, due.Month, 1) : t.CreatedUtc.ToSydney().Date;
        if (UDate("Scheduled Date") is DateTime scheduled && scheduled < from) from = scheduled;
        return (from, t.CompletedUtc?.ToSydney().Date ?? UDate("Performed Date"));
    }

    // Work tab: assets serviced and repairs resolved, of all of them
    public int WorkDone => ServiceAssets.Count(a => Performed(a.Asset) != null) + Repairs.Count(r => !r.Active);
    public int WorkTotal => ServiceAssets.Count + Repairs.Count;

    // Uptick invoices raised for this task, and forms submitted on it (with who submitted them)
    public IReadOnlyList<Invoice> Invoices { get; set; } = [];
    public IReadOnlyList<(DynamicFormResponse Form, string? Submitter)> Forms { get; set; } = [];

    // Technicians (Uptick users) to assign, for admins
    public IReadOnlyList<(string Id, string Name)> Techs { get; set; } = [];

    // The task's Uptick row by header, for what only Uptick has (notes, tags, authorisation, billing card, ...)
    public IReadOnlyDictionary<string, string> Uptick { get; set; } = new Dictionary<string, string>();

    public string? U(string header) => Uptick.TryGetValue(header, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public static IReadOnlyDictionary<string, string> ParseUptick(string? json)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, value) in UptickJson.Parse(json)) values.TryAdd(header, value);
        return values;
    }

    // Uptick's status (READY, SCHEDULED, ...) for imported tasks, else FyreApp's
    public string StatusLabel => U("Status")?.ToUpperInvariant() ?? Task.Status switch
    {
        ClientTaskStatus.InProgress => "IN PROGRESS",
        var s => s.ToString().ToUpperInvariant()
    };

    // Uptick shows the task's name, then its description's first line beside it in lighter text
    public string? Subtitle
    {
        get
        {
            // (skipping a heading such as "Scope of works:", which generated routine tasks start with)
            var line = (U("Description") ?? Task.Description)?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => !l.EndsWith(':'))?.TrimStart('-', ' ');
            return line == null || line == Task.Title ? null : line.Length > 80 ? line[..79] + "…" : line;
        }
    }

    // Scope of works: Uptick's column, else the FyreApp description
    public string? ScopeOfWorks => Scope(Task);

    public static string? Scope(ClientTask t) => t.ExternalId != null ? t.ScopeOfWorks : t.Description;

    public IReadOnlyList<string> Tags =>
        (U("Tags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Initials(string? name)
    {
        var bare = FyreApp.Services.UserManagement.UptickLoginSync.BareName(name ?? "");
        var parts = bare.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
        };
    }
}
