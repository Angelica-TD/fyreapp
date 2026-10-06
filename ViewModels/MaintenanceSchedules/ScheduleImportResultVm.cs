namespace FyreApp.ViewModels.MaintenanceSchedules;

public class ScheduleImportResultVm
{
    public bool DryRun { get; set; }
    public string? FileName { get; set; }

    // Fatal error (file unreadable, unsupported type, database error)
    public string? Error { get; set; }

    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int SkippedExisting { get; set; }
    public int SkippedSiteNotFound { get; set; }
    public int SkippedUnsupportedFrequency { get; set; }
    public int SkippedInvalid { get; set; }

    // Sites matched by client + property name that get their ExternalId set from "Property ref"
    public int SitesLinked { get; set; }

    // Existing schedules newly linked to Uptick routines they cover
    public int RoutineLinksAdded { get; set; }

    // Routine occurrences (one per row) added / refreshed
    public int OccurrencesCreated { get; set; }
    public int OccurrencesUpdated { get; set; }

    public List<string> NewIntervals { get; set; } = new();
    public List<ScheduleImportItemVm> Items { get; set; } = new();
    public List<ScheduleImportIssueVm> Issues { get; set; } = new();
}

public class ScheduleImportItemVm
{
    public string ClientName { get; set; } = "";
    public string SiteName { get; set; } = "";
    public string? PropertyRef { get; set; }
    public string IntervalName { get; set; } = "";
    public DateTime NextRunDate { get; set; }
    public List<string> Routines { get; set; } = new();

    // true = created / would create; false = skipped because the site already has a schedule for this interval
    public bool WillCreate { get; set; }
}

public class ScheduleImportIssueVm
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Message { get; set; } = "";
    public List<int> Rows { get; set; } = new();
}
