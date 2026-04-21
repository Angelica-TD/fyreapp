using System.ComponentModel.DataAnnotations;

namespace FyreApp.Services.ServiceOfferings;

public class ServiceOfferingViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public List<IntervalSummary> Intervals { get; set; } = new();
}

public class IntervalSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Months { get; set; }
}

public class ServiceOfferingFormDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<int> SelectedIntervalIds { get; set; } = new();
}
