using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A subcontracting business (Uptick "contractors"). Tasks and routines name them as "Contractor" / "Subcontractor".
public class Contractor : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

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
}
