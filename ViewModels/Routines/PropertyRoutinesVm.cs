namespace FyreApp.ViewModels.Routines;

// A property's routines for one year, laid out like Uptick's property Routines tab: one row per routine,
// grouped by service group, with a marker in each month a routine is due.
public class PropertyRoutinesVm
{
    public int SiteId { get; set; }
    public int Year { get; set; }

    // Sydney today, to highlight the current month and work out what's overdue
    public DateTime Today { get; set; }

    public List<PropertyRoutineGroupVm> Groups { get; set; } = new();

    // Routine tasks due in each month (index 0 = January)
    public List<PropertyRoutineTaskVm>[] TasksByMonth { get; set; } =
        Enumerable.Range(0, 12).Select(_ => new List<PropertyRoutineTaskVm>()).ToArray();
}

// e.g. "Servicing - Portables & Fire Equipment"
public class PropertyRoutineGroupVm
{
    public string Name { get; set; } = string.Empty;

    // Under the group name, as in Uptick: who usually does it and the estimated time for the year
    public string? Technician { get; set; }
    public int EstimatedMinutes { get; set; }

    public List<PropertyRoutineRowVm> Rows { get; set; } = new();
}

// e.g. "10 - Portable and Wheeled Fire Extinguishers"
public class PropertyRoutineRowVm
{
    public string Routine { get; set; } = string.Empty;

    // Markers per month (index 0 = January)
    public List<PropertyRoutineMarkerVm>[] Months { get; set; } =
        Enumerable.Range(0, 12).Select(_ => new List<PropertyRoutineMarkerVm>()).ToArray();
}

// Uptick's legend
public enum RoutineMarkerState
{
    Forecasted,
    TaskCreated,
    CompletedWithinTolerance,
    CompletedOutsideTolerance,
    NotCompletedOutsideTolerance
}

// One routine occurrence, e.g. "A" (Annual) due 31 Aug
public class PropertyRoutineMarkerVm
{
    public int Id { get; set; }

    // Uptick's level code, e.g. "A" for Annual, "H" for Six-monthly
    public string Code { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public RoutineMarkerState State { get; set; }
    public int? TaskId { get; set; }
}

public class PropertyRoutineTaskVm
{
    public int Id { get; set; }
    public string? Ref { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public bool Completed { get; set; }
}
