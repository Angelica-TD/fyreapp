using FyreApp.Models;
using Microsoft.AspNetCore.Identity;

namespace FyreApp.Data;

public static class IdentitySeed
{
    public static async Task SeedAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Roles
        foreach (var role in new[] { "Admin", "Tech", "Developer" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        // Admin user
        await EnsureUser(userManager, "office@fyrepower.com.au", "ChangeMe!12345", "Admin");

        // Developer user
        await EnsureUser(userManager, "hello@techsea.com.au", "D3v@Fyre9kXm!2q", "Developer");
    }

    private static async Task EnsureUser(
        UserManager<ApplicationUser> userManager,
        string email, string password, string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded) return;
        }

        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);
    }
}
