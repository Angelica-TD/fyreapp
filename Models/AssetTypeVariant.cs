using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// Variant of an asset type, e.g. "DCP AB(E) 4.5KG" for "Fire Extinguisher" (Uptick asset type variants export)
public class AssetTypeVariant
{
    public int Id { get; set; }

    // Maps from Uptick asset type variants export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    public int AssetTypeId { get; set; }
    public AssetType AssetType { get; set; } = null!;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? DefaultReplacementProduct { get; set; }

    public bool Active { get; set; } = true;

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }

    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
