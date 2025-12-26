using System;

namespace FyreApp.Models;

public class Asset
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // FK → Site (one site only)
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    // Many-to-many
    public ICollection<AssetType> AssetTypes { get; set; } = new List<AssetType>();
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; }

}
