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

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Tech";
}
