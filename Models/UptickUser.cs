using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A login in Uptick (Uptick "Users" export): office staff, technicians, contractors and client portal users.
// Kept as a record of who's who; it doesn't give anyone a FyreApp login. A FyreApp user with the same
// email is the same person.
public class UptickUser
{
    public int Id { get; set; }

    // Maps from Uptick users export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // As tasks, reports and routines name them, e.g. "Wes Speedon (Field Tech)"
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Email { get; set; }

    // Uptick's licence type: DESK (office), FIELD (technician), CONTRACTOR or CUSTOMER (client portal)
    [StringLength(20)]
    public string? LicenseType { get; set; }

    // e.g. "Technician - Technician - Higher Permissions" (Uptick joins group names with " - ")
    [StringLength(500)]
    public string? SecurityGroups { get; set; }

    // e.g. "Servicing - Portables & Fire Equipment"
    [StringLength(200)]
    public string? ServiceGroup { get; set; }

    [StringLength(200)]
    public string? HomeBranch { get; set; }

    [StringLength(500)]
    public string? AllowedBranches { get; set; }

    public decimal? CostRate { get; set; }
    public decimal? SellRate { get; set; }

    // Trade licence, e.g. "QBCC 15366855" (the export's second "License" column)
    [StringLength(200)]
    public string? Licence { get; set; }

    public DateTime? JoinedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    // Every column of the Uptick export row as JSON (key order kept)
    public string? UptickData { get; set; }

    public static string LicenseLabel(string? licenseType) => licenseType?.ToUpperInvariant() switch
    {
        "DESK" => "Desk",
        "FIELD" => "Field",
        "CONTRACTOR" => "Contractor",
        "CUSTOMER" => "Customer",
        null or "" => "-",
        _ => licenseType
    };
}
