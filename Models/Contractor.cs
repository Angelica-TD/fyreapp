using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A subcontracting business (Uptick "Contractors" export). Tasks and routines name them as "Contractor" / "Subcontractor".
public class Contractor
{
    public int Id { get; set; }

    // Maps from Uptick contractors export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? PrimaryContact { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    // One address per line
    [StringLength(1000)]
    public string? EmailCc { get; set; }

    // Uptick "Phone BH"
    [StringLength(100)]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string? Mobile { get; set; }

    // e.g. "General"
    [StringLength(100)]
    public string? Category { get; set; }

    [StringLength(500)]
    public string? Tags { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(1000)]
    public string? Skills { get; set; }

    [StringLength(1000)]
    public string? ServiceAreas { get; set; }

    public bool Starred { get; set; }
    public bool Active { get; set; } = true;

    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }
}
