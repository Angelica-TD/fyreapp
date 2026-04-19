using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.Clients;

public class CreateClientVm
{
    [Required]
    [StringLength(200)]
    public string? Name { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Primary Contact Name")]
    public string? PrimaryContactName { get; set; }

    [Required, StringLength(320), EmailAddress]
    [Display(Name = "Email")]
    public string? PrimaryContactEmail { get; set; }

    [StringLength(32)]
    [Display(Name = "Mobile")]
    public string? PrimaryContactMobile { get; set; }

    public ClientVm? ExistingClient { get; set; }
}
