using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.Clients;

public class CreateClientVm
{
    [Required]
    [StringLength(100)]
    public string? Name { get; set; }

    public ClientVm? ExistingClient { get; set; }
}
