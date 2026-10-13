namespace FyreApp.Infrastructure;

// Parts of the UI that are switched off for now
public static class UiFeatures
{
    // FyreApp's maintenance schedules (property/asset + interval). Hidden in favour of Uptick-style routines;
    // they still exist behind the scenes (routine imports, quote acceptance and task completion maintain them).
    public static readonly bool MaintenanceSchedules = false;
}
