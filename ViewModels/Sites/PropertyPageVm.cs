using FyreApp.Models;
using FyreApp.ViewModels.Routines;

namespace FyreApp.ViewModels.Properties;

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

    public required Property Property { get; init; }
    public required string Tab { get; init; }

    // Summary: the most severe open remark and how many are open
    public int OpenRemarks { get; init; }
    public Remark? MostSevereRemark { get; init; }

    // Property tab
    public IReadOnlyList<TimelineEvent> Timeline { get; init; } = [];
    public bool NotesOnly { get; init; }

    // Uptick's custom fields with a value for this property (extra fields export + its Uptick row)
    public IReadOnlyList<FyreApp.Services.Imports.ExtraFieldValue> ExtraFields { get; init; } = [];

    // Assets tab (filters as on Uptick's: active, asset type, search)
    public IReadOnlyList<Asset> Assets { get; init; } = [];
    public IReadOnlyList<(int Id, string Name)> AssetTypes { get; init; } = [];
    public string AssetActive { get; init; } = "yes";
    public int? AssetTypeId { get; init; }
    public string? AssetSearch { get; init; }

    // Remarks (under Assets)
    public IReadOnlyList<Remark> Remarks { get; init; } = [];
    public bool ShowClosedRemarks { get; init; }

    // Routines tab
    public PropertyRoutinesVm? Routines { get; init; }

    // Billing tab (Uptick's billing exports)
    public const int InvoicesShown = 100;
    public BillingCard? PropertyBillingCard { get; init; }
    public BillingCard? ClientBillingCard { get; init; }
    public IReadOnlyList<BillingContract> BillingContracts { get; init; } = [];
    public IReadOnlyList<BillingContractLineItem> BillingContractLines { get; init; } = [];
    public IReadOnlyList<Invoice> Invoices { get; init; } = [];
    public int InvoiceCount { get; init; }
    public IReadOnlyList<CreditNote> CreditNotes { get; init; } = [];

    // Records tab: service reports, Uptick's quotes and the property's schedules
    public IReadOnlyList<DefectQuote> DefectQuotes { get; init; } = [];
    public IReadOnlyList<ServiceQuote> ServiceQuotes { get; init; } = [];
    public IReadOnlyList<PropertySchedule> Schedules { get; init; } = [];

    // Records tab (service reports) and Tasks tab
    public IReadOnlyList<Report> Reports { get; init; } = [];

    // The reports' PDFs FyreApp holds, by report Uptick id: the file's id
    public IReadOnlyDictionary<long, int> ReportPdfs { get; set; } = new Dictionary<long, int>();
    public IReadOnlyList<ClientTask> Tasks { get; init; } = [];
}

