using FyreApp.Models;

namespace FyreApp.ViewModels.Field;

// The field app's day (/Field): the technician's visits on one day
public class FieldDayVm
{
    // The day shown, Sydney time (date only)
    public DateTime Day { get; set; }
    public string? Technician { get; set; }

    // The signed-in login isn't linked to an Uptick user, so it has no day
    public bool NotLinked { get; set; }

    public IReadOnlyList<FieldVisit> Visits { get; set; } = [];

    // The task the technician's clock is running on, wherever it is
    public FieldVisit? Working { get; set; }
}

// A task to do on the day: booked (an appointment), or assigned to the technician and scheduled for that day
public sealed record FieldVisit(
    int TaskId,
    string? TaskRef,
    string Title,
    string? Category,
    string PropertyName,
    string? Address,
    DateTime? StartLocal,
    DateTime? EndLocal,
    string? Notes,
    ClientTaskStatus Status);

// One asset to service for one of the task's routines, and what the technician found
public sealed record FieldLine(
    int AssetId,
    string Asset,
    string? AssetRef,
    string? Location,
    string Routine,
    string? Result,
    string? Notes,
    int OpenRemarks);

// The field app's task page (/Field/Task/{id})
public class FieldTaskVm
{
    public ClientTask Task { get; set; } = null!;
    public IReadOnlyList<PropertyContact> Contacts { get; set; } = [];
    public IReadOnlyList<Appointment> Appointments { get; set; } = [];

    // The signed-in technician's running session on this task, and their time on it today
    public TaskSession? Running { get; set; }
    public IReadOnlyList<TaskSession> MyTimeToday { get; set; } = [];

    // Running on another task: starting here finishes it
    public FieldVisit? RunningElsewhere { get; set; }

    public IReadOnlyList<FieldLine> Lines { get; set; } = [];

    // Why the signed-in login can't record work here (none when it can)
    public string? CannotWork { get; set; }

    public string? MapsUrl => Task.Property is { } p
        ? "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(
            p.Latitude is { } lat && p.Longitude is { } lng ? $"{lat},{lng}" : p.AddressDisplay ?? p.Name)
        : null;
}
