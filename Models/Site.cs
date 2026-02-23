using System;
using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    // Address (Google + manual)
    [StringLength(300)]
    public string? AddressDisplay { get; set; } 

    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [StringLength(80)]
    public string? Suburb { get; set; }

    [StringLength(10)]
    public string? Postcode { get; set; }

    [StringLength(20)]
    public string? State { get; set; }

    [StringLength(64)]
    public string? GooglePlaceId { get; set; }

    // FK → Client
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    // Navigation
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; }
}
