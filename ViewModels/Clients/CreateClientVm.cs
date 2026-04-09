using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.Clients;

public class CreateClientVm : IValidatableObject
{
    [Required]
    [StringLength(200)]
    public string? Name { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Primary Contact Name")]
    public string? PrimaryContactName { get; set; }

    [StringLength(320), EmailAddress]
    [Display(Name = "Email")]
    public string? PrimaryContactEmail { get; set; }

    [StringLength(32)]
    [Display(Name = "Mobile")]
    public string? PrimaryContactMobile { get; set; }

    public ClientVm? ExistingClient { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PrimaryContactEmail) && string.IsNullOrWhiteSpace(PrimaryContactMobile))
            yield return new ValidationResult(
                "Please provide either a mobile number or an email address.",
                [nameof(PrimaryContactEmail), nameof(PrimaryContactMobile)]);
    }
}
