using System;

namespace FyreApp.Models;

public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // FK → Client
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    // Navigation
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; }
}
