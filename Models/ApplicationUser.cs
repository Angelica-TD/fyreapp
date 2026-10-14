using Microsoft.AspNetCore.Identity;

namespace FyreApp.Models;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // The Uptick user this login belongs to. Uptick is the source of truth for who's who: a linked login
    // takes its name and email from Uptick (refreshed on each Users import).
    public int? UptickUserId { get; set; }
    public UptickUser? UptickUser { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}
