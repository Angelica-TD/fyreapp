using System;
using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class Asset
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Used for import matching (maps from Uptick asset export "ID")
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "A-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: the Uptick ID if imported, otherwise the FyreApp ref
    public string? DisplayRef => string.IsNullOrWhiteSpace(ExternalId) ? FyreRef : ExternalId;

    // Uptick asset ref within the property (e.g. "4")
    public string? Ref { get; set; }
    public string? Location { get; set; }
    public string? Barcode { get; set; }
    public string? Variant { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Size { get; set; }

    // Last inspection result from Uptick (e.g. "Pass", "Fail")
    public string? Compliance { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? BaseDate { get; set; }
    public DateTime? InstallationDate { get; set; }
    public DateTime? LastServiceDate { get; set; }

    // FK → Site (one site only)
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    // Many-to-many
    public ICollection<AssetType> AssetTypes { get; set; } = new List<AssetType>();
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; }
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();

}
