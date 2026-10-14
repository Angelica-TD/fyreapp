namespace FyreApp.Services.UserManagement;

public class UserViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => (!string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName))
        ? $"{FirstName} {LastName}".Trim()
        : Email;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    // The Uptick user this login is linked to; when set, name and email come from Uptick
    public int? UptickUserId { get; set; }
    public string? UptickName { get; set; }
}
