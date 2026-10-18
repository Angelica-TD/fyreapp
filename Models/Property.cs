using System;
using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class Property
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Used for import matching (maps from Uptick export "Property ref")
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "P-1001"), assigned by the database when there's no Uptick ref
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Created because another export referenced this record but it wasn't in its own export
    // (e.g. archived in Uptick). Inactive; filled in when the record itself is imported.
    public bool IsPlaceholder { get; set; }

    // Every column of the Uptick export row as JSON (key order kept), so nothing is lost on import
    public string? UptickData { get; set; }

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

    // Uptick sometimes has the full name, e.g. "Australian Capital Territory"
    [StringLength(100)]
    public string? State { get; set; }

    [StringLength(300)]
    public string? GooglePlaceId { get; set; }

    public bool Active { get; set; } = true;

    // Uptick property status: ACTIVE, ONHOLD, SETUP or INACTIVE (Active is false only for INACTIVE)
    [StringLength(20)]
    public string? Status { get; set; }

    // When the property was created (Uptick's "Created" for imported ones)
    public DateTime? Created { get; set; }

    // When it went inactive or on hold (Uptick "Inactive/Onhold Date")
    public DateTime? InactiveDate { get; set; }

    // Map position (Uptick "Address Lat" / "Address Lng")
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Property access, for techs. Schedule and procedure are Uptick's codes, e.g. "NR" (needs review), "CA".
    [StringLength(20)]
    public string? AccessSchedule { get; set; }

    [StringLength(20)]
    public string? AccessProcedure { get; set; }

    [StringLength(100)]
    public string? AccessCode { get; set; }

    public string? AccessNote { get; set; }

    // Office notes and requirements (Uptick "Internal Note", "Service / Safety / Billing Requirements")
    public string? InternalNote { get; set; }
    public string? ServiceRequirements { get; set; }
    public string? SafetyRequirements { get; set; }
    public string? BillingRequirements { get; set; }

    // FK → Client
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    // Navigation
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; } = new List<MaintenanceSchedule>();
    public ICollection<PropertyContact> Contacts { get; set; } = new List<PropertyContact>();
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();
    public ICollection<ServiceReport> ServiceReports { get; set; } = new List<ServiceReport>();
}
