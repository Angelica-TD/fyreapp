using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.Tasks;

public class EditClientTaskVm
{
    public int Id { get; set; }

    [Required]
    public int ClientId { get; set; }

    [Required]
    public int SiteId { get; set; }

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public ClientTaskPriority Priority { get; set; } = ClientTaskPriority.Normal;

    public DateTime? DueDateLocal { get; set; }

    public ClientTaskStatus Status { get; set; } = ClientTaskStatus.Open;
}
