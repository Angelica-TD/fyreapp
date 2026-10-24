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

    // Every other open task assigned to them, whatever the day: soonest scheduled or due first
    public IReadOnlyList<FieldVisit> Assigned { get; set; } = [];

    // Theirs completed on the day (not on its list already): shown greyed out, read only
    public IReadOnlyList<FieldVisit> Done { get; set; } = [];

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
    ClientTaskStatus Status,
    // For an open task listed under "Your other open tasks": when it's scheduled, and due (Sydney dates)
    DateTime? Scheduled = null,
    DateTime? Due = null);

// One asset to service for one of the task's routines, and what the technician found
public sealed record FieldLine(
    int AssetId,
    string Asset,
    string? AssetRef,
    string? Location,
    string Routine,
    string? Result,
    string? Notes,
    IReadOnlyList<FieldRemark> Remarks);

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

    // Open remarks raised on this task that aren't on an asset in Lines (on the property, or another asset)
    public IReadOnlyList<FieldRemark> OtherRemarks { get; set; } = [];

    public IReadOnlyList<FieldTaskLine> TaskLines { get; set; } = [];
    public IReadOnlyList<FieldStockUse> StockUsed { get; set; } = [];

    // What can be recorded as used (when the technician can work here)
    public IReadOnlyList<FieldProduct> Products { get; set; } = [];

    // Forms submitted on the task, and those that can be filled in
    public IReadOnlyList<FyreApp.Services.Forms.FieldFormSummary> Forms { get; set; } = [];
    public IReadOnlyList<FyreApp.Services.Forms.FormTemplateOption> FormTemplates { get; set; } = [];

    // The customer's sign-off, once taken
    public TaskSignOff? SignOff { get; set; }

    // Why the signed-in login can't record work here (none when it can)
    public string? CannotWork { get; set; }

    public string? MapsUrl => Task.Property is { } p
        ? "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(
            p.Latitude is { } lat && p.Longitude is { } lng ? $"{lat},{lng}" : p.AddressDisplay ?? p.Name)
        : null;
}

// One of the task's lines (Uptick's subtasks): a routine due, or a product, ticked off once done
public sealed record FieldTaskLine(int Id, string Label, DateTime? Due, bool IsPerformed);

// Stock used on the task; Removable when the signed-in technician recorded it in FyreApp
public sealed record FieldStockUse(int Id, string Product, decimal Quantity, string? By, DateTime? WhenUtc, bool Removable);

public sealed record FieldProduct(int Id, string Name, string? Code);

// An asset on the job (/Field/Task/{id}/Asset/{assetId}): what it is, its result for each of the job's routines, its
// remarks, photos and history
public class FieldAssetVm
{
    public required FieldTaskVm Job { get; init; }
    public required Asset Asset { get; init; }

    // This asset's lines on the job (one per routine); none when it's at the property but not on the job
    public IReadOnlyList<FieldLine> Lines { get; init; } = [];
    public IReadOnlyList<FieldRemark> OpenRemarks { get; init; } = [];

    // Its service on every task, newest first
    public IReadOnlyList<FieldAssetServiceLine> Service { get; init; } = [];

    // Remarks no longer open, newest first
    public IReadOnlyList<Remark> PastRemarks { get; init; } = [];

    public IReadOnlyList<TimelineEvent> Timeline { get; init; } = [];

    public bool CanWork => Job.CannotWork == null;
}

// One time the asset was serviced: on which task, for which routine, the result and who
public sealed record FieldAssetServiceLine(DateTime? WhenUtc, int TaskId, string? TaskRef, string Routine, string? Result, string? Notes, string? By);

// One asset line's Pass / Fail / N/A form (_ResultForm), on the job and on the asset's page. Key makes its inputs'
// ids unique on the page; BackToAsset: raising a remark from it returns to the asset's page.
public sealed record FieldResultForm(int TaskId, FieldLine Line, string Key, bool CanWork, int? BackToAsset = null);
