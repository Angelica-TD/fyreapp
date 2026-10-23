using FyreApp.Models;

namespace FyreApp.ViewModels.Lists;

// Uptick's Reports list has no default filters, only search
public class ReportFilter
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;

    public IEnumerable<KeyValuePair<string, string>> QueryValues()
    {
        yield return FilterQuery.Kv("search", Search);
    }
}

public class ReportListItemVm
{
    public int Id { get; set; }
    public string? Ref { get; set; }
    public string? Title { get; set; }
    public bool? Compliant { get; set; }
    public DateTime? Issued { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyRef { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientContact { get; set; }
    public string? TaskRef { get; set; }
    public long? UptickId { get; set; }

    // Its PDF, when FyreApp holds a copy
    public int? PdfFileId { get; set; }
}

// A report's page: what it is, each asset's line on it, and its PDF
public class ReportPageVm
{
    public Report Report { get; init; } = null!;
    public IReadOnlyList<ReportItem> Items { get; init; } = [];
    public int? PdfFileId { get; init; }

    // For a report made in the field app
    public ReportWorkVm? Work { get; init; }
}

public class ReportListVm
{
    public ReportFilter Filter { get; set; } = new();
    public IReadOnlyList<ReportListItemVm> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);
}

// What was done on the task, shown with a report made by completing it in the field app (Uptick's own reports have
// their PDF instead)
public class ReportWorkVm
{
    public int TaskId { get; init; }

    // Remarks raised on the task (as found), and those resolved on it
    public IReadOnlyList<ReportRemark> Raised { get; init; } = [];
    public IReadOnlyList<ReportRemark> Resolved { get; init; } = [];

    public IReadOnlyList<FyreApp.Services.Forms.FieldFormSummary> Forms { get; init; } = [];
    public TaskSignOff? SignOff { get; init; }

    // Time recorded on the task, on site and travelling
    public decimal OnSiteHours { get; init; }
    public decimal TravelHours { get; init; }
}

public sealed record ReportRemark(int Id, string? Ref, string? Asset, string? Type, string? Severity, string? Description, string? Resolution);
