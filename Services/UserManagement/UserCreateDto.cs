using System.ComponentModel.DataAnnotations;

namespace FyreApp.Services.UserManagement;

// Gives an Uptick user a FyreApp login; their name and email come from Uptick
public class UserCreateDto
{
    [Required]
    public int UptickUserId { get; set; }

    [Phone]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    // Email them a link to choose their own password (the welcome email); otherwise the admin sets Password now
    public bool EmailPasswordLink { get; set; } = true;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required]
    public string Role { get; set; } = "Tech";
}
