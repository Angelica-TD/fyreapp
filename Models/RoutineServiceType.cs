using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// An Uptick routine, e.g. "10 - Portable and Wheeled Fire Extinguishers" (Uptick routine service types export).
// Uptick reference data; FyreApp's own ServiceType (quoting) is separate.
public class RoutineServiceType
{
    public int Id { get; set; }

    // Maps from Uptick routine service types export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    // e.g. "AS1851-2012"
    [StringLength(100)]
    public string? Standard { get; set; }

    // e.g. "AS1851-2012 Section 6"
    [StringLength(100)]
    public string? StandardReference { get; set; }

    public string? StandardNotes { get; set; }

    // e.g. "NCC (BCA) Clause 2.2, AS1670"
    [StringLength(200)]
    public string? DefaultPerformanceStandard { get; set; }

    // e.g. "Servicing - Portables & Fire Equipment"
    [StringLength(100)]
    public string? ServiceGroup { get; set; }

    public bool Custom { get; set; }

    public bool Active { get; set; } = true;

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }

    public ICollection<RoutineServiceLevel> Levels { get; set; } = new List<RoutineServiceLevel>();
}
