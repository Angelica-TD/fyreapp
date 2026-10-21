using System.ComponentModel.DataAnnotations;

namespace FyreApp.ViewModels.Auth;

// Choosing a password from an emailed link (a new login's welcome email, or a link sent from the Users page)
public class SetPasswordVm
{
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;

    // Shown, not posted back: who's setting the password
    public string? Email { get; set; }

    // The link is unknown, used or past its 3 days
    public bool Expired { get; set; }

    [Required]
    [MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
