using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class ServiceType
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? AS1851Section { get; set; }

    public bool GeneratesComplianceReport { get; set; }

    [MaxLength(50)]
    public string? ComplianceFormReference { get; set; }

    public int? DefaultIntervalId { get; set; }
    public MaintenanceInterval? DefaultInterval { get; set; }
}
