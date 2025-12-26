using System;

namespace FyreApp.Models;

public class AssetType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Many-to-many
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
