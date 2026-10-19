using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.Assets;

// The asset page, laid out like Uptick's: the asset on the left, tabs (Asset, Remark history, Service History) on the right
public class AssetPageVm
{
    public static readonly (string Key, string Label)[] Tabs =
    {
        ("asset", "Asset"), ("defects", "Defect history"), ("service", "Service History")
    };

    public static string NormaliseTab(string? tab) => Tabs.Any(t => t.Key == tab) ? tab! : "asset";

    public required Asset Asset { get; init; }
    public required string Tab { get; init; }

    // "INFORMATIONAL is highest severity defect (identified 1 month ago)"
    public Remark? MostSevereOpenRemark { get; init; }

    // Asset tab
    public string? LastServiceBy { get; init; }
    public IReadOnlyList<Remark> OpenRemarks { get; init; } = [];
    public IReadOnlyList<TimelineEvent> Timeline { get; init; } = [];
    public bool NotesOnly { get; init; }

    // Uptick's custom fields with a value for this asset (extra fields export + the asset's Uptick row)
    public IReadOnlyList<FyreApp.Services.Imports.ExtraFieldValue> ExtraFields { get; init; } = [];

    // Remark history tab: every defect, open first
    public IReadOnlyList<Remark> Remarks { get; init; } = [];
}

// Uptick's asset Update form
public class AssetEditVm
{
    public int Id { get; set; }

    [Display(Name = "Type")]
    public int? AssetTypeId { get; set; }

    [Display(Name = "Variant")]
    public int? AssetTypeVariantId { get; set; }

    [StringLength(500)]
    public string? Location { get; set; }

    // Blank uses the asset type and variant
    [StringLength(500)]
    public string? Label { get; set; }

    public bool Active { get; set; } = true;

    [Display(Name = "Internal Reference"), StringLength(100)]
    public string? Ref { get; set; }

    [StringLength(100)]
    public string? Serial { get; set; }

    [StringLength(100)]
    public string? Barcode { get; set; }

    [Display(Name = "Base Date"), DataType(DataType.Date)]
    public DateTime? BaseDate { get; set; }

    [Display(Name = "Installation Date"), DataType(DataType.Date)]
    public DateTime? InstallationDate { get; set; }

    [Display(Name = "Inspection Order")]
    public int? InspectionOrder { get; set; }

    [Display(Name = "Internal Note")]
    public string? InternalNote { get; set; }

    [StringLength(200)]
    public string? Make { get; set; }

    [StringLength(200)]
    public string? Model { get; set; }

    [StringLength(200)]
    public string? Size { get; set; }

    [StringLength(100)]
    public string? Quantity { get; set; }

    // For the page around the form
    public Asset? Asset { get; set; }
    public IReadOnlyList<(int Id, string Name)> AssetTypes { get; set; } = [];
    public IReadOnlyList<(int Id, int AssetTypeId, string Name)> Variants { get; set; } = [];
}
