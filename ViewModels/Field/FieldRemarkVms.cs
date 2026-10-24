using FyreApp.Models;

namespace FyreApp.ViewModels.Field;

// An open remark on an asset the technician is servicing
public sealed record FieldRemark(int Id, string? Ref, string? Type, string? Description, string? SeverityLabel, bool RaisedHere);

public sealed record RemarkTypeOption(int Id, string Label, string? SeverityLabel, bool ForAsset);

// What a technician fills in to raise a remark
public class RaiseRemarkInput
{
    public int? AssetId { get; set; }
    public int? RemarkTypeId { get; set; }

    // A remark type of their own, typed in when none of the list fits (none picked from it)
    public string? CustomType { get; set; }
    public int? Severity { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
}

// Raising a remark on a task, on one of the property's assets or the property itself (/Field/Task/{id}/Remark)
public class RaiseRemarkVm
{
    public int TaskId { get; set; }
    public string? TaskRef { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public Asset? Asset { get; set; }

    // The asset's own remark types first (ForAsset), then the rest
    public IReadOnlyList<RemarkTypeOption> Types { get; set; } = [];

    public RaiseRemarkInput Input { get; set; } = new();
}

// Resolving an open remark on a task (/Field/Task/{id}/Remark/{remarkId}/Resolve)
public class ResolveRemarkVm
{
    public int TaskId { get; set; }
    public string? TaskRef { get; set; }
    public Remark Remark { get; set; } = null!;
    public string? Resolution { get; set; }
}

// Views/Field/_Remarks.cshtml: open remarks listed on the field page, each with a Resolve button
// BackToAsset: shown on an asset's page, which resolving returns to
public sealed record FieldRemarksList(int TaskId, IReadOnlyList<FieldRemark> Remarks, bool CanWork, int? BackToAsset = null);
