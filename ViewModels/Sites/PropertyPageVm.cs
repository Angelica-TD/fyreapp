using FyreApp.Models;
using FyreApp.ViewModels.Routines;

namespace FyreApp.ViewModels.Sites;

// The property page, laid out like Uptick's: a tab bar, one tab's content at a time
public class PropertyPageVm
{
    // Tabs, in Uptick's order. Remarks sit under Assets ("Open remarks"), as in Uptick.
    public static readonly (string Key, string Label)[] Tabs =
    {
        ("property", "Property"), ("assets", "Assets"), ("routines", "Routines"),
        ("billing", "Billing"), ("records", "Records"), ("tasks", "Tasks")
    };

    public static string NormaliseTab(string? tab) =>
        tab == "remarks" || Tabs.Any(t => t.Key == tab) ? tab! : "property";

    public required Site Site { get; init; }
    public required string Tab { get; init; }

    // Summary: the most severe open remark and how many are open
    public int OpenRemarks { get; init; }
    public Defect? MostSevereRemark { get; init; }

    // Property tab
    public IReadOnlyList<TimelineEntry> Timeline { get; init; } = [];
    public bool NotesOnly { get; init; }

    // Assets tab (filters as on Uptick's: active, asset type, search)
    public IReadOnlyList<Asset> Assets { get; init; } = [];
    public IReadOnlyList<(int Id, string Name)> AssetTypes { get; init; } = [];
    public string AssetActive { get; init; } = "yes";
    public int? AssetTypeId { get; init; }
    public string? AssetSearch { get; init; }

    // Remarks (under Assets)
    public IReadOnlyList<Defect> Remarks { get; init; } = [];
    public bool ShowClosedRemarks { get; init; }

    // Routines tab
    public PropertyRoutinesVm? Routines { get; init; }

    // Records tab (service reports) and Tasks tab
    public IReadOnlyList<ServiceReport> Reports { get; init; } = [];
    public IReadOnlyList<ClientTask> Tasks { get; init; } = [];
}

