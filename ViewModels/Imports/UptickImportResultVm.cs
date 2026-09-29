using FyreApp.Services.Imports;

namespace FyreApp.ViewModels.Imports;

public class UptickImportResultVm
{
    public UptickExportType? Type { get; set; }
    public bool DryRun { get; set; }
    public string? FileName { get; set; }

    // Fatal error (file unreadable, wrong export, database error); nothing was saved
    public string? Error { get; set; }

    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int SkippedExisting { get; set; }

    // Row-level skips by reason, e.g. "Property not found" -> 3
    public Dictionary<string, int> Skipped { get; set; } = new();

    // Extra outcomes worth calling out, e.g. "Linked 4 existing properties to their Uptick ref"
    public List<string> Notes { get; set; } = new();

    public List<ImportIssueVm> Issues { get; set; } = new();
}

public class ImportIssueVm
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Message { get; set; } = "";
    public List<int> Rows { get; set; } = new();
}
