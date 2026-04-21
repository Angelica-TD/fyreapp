using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class ServiceOffering
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<MaintenanceInterval> Intervals { get; set; } = new List<MaintenanceInterval>();
}
