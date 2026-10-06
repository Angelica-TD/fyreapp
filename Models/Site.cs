using System;
using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Used for import matching (maps from Uptick export "Property ref")
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "P-1001"), assigned by the database when there's no Uptick ref
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: the Uptick ref if imported, otherwise the FyreApp ref
    public string? DisplayRef => string.IsNullOrWhiteSpace(ExternalId) ? FyreRef : ExternalId;

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

    [StringLength(300)]
    public string? GooglePlaceId { get; set; }

    public bool Active { get; set; } = true;

    // FK → Client
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    // Navigation
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; } = new List<MaintenanceSchedule>();
    public ICollection<SiteContact> Contacts { get; set; } = new List<SiteContact>();
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();
    public ICollection<ServiceReport> ServiceReports { get; set; } = new List<ServiceReport>();
}
