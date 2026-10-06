using System;
using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// Kind of asset, e.g. "Fire Extinguisher". Created by name from the assets export, and filled in
// from Uptick's asset types export (reference data).
public class AssetType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Maps from Uptick asset types export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // e.g. "AS1851 Sec 06 - Fire Detection and Alarm Systems"
    [StringLength(200)]
    public string? Category { get; set; }

    // e.g. "AS1851-2012, Section 6"
    [StringLength(200)]
    public string? InspectionCriteria { get; set; }

    // e.g. "Fire Detection"
    [StringLength(100)]
    public string? SequenceGroup { get; set; }

    public string? Description { get; set; }

    public bool Active { get; set; } = true;

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }

    // Many-to-many
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();

    public ICollection<AssetTypeVariant> Variants { get; set; } = new List<AssetTypeVariant>();
}
