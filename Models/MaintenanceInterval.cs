using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class MaintenanceInterval
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    [Required]
    public int Months { get; set; }
    public ICollection<MaintenanceSchedule> MaintenanceSchedules { get; set; }
}
