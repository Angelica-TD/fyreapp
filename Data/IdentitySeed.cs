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

        const string adminRole = "Admin";
        const string adminEmail = "office@fyrepower.com.au";
        const string adminPassword = "ChangeMe!12345";

        const string techRole = "Tech";

        if (!await roleManager.RoleExistsAsync(adminRole))
            await roleManager.CreateAsync(new IdentityRole(adminRole));
        
        if (!await roleManager.RoleExistsAsync(techRole))
            await roleManager.CreateAsync(new IdentityRole(techRole));

        var user = await userManager.FindByEmailAsync(adminEmail);
        if (user == null)
        {
            user = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var created = await userManager.CreateAsync(user, adminPassword);
            if (!created.Succeeded) return; // can log created.Errors if wanted
        }

        if (!await userManager.IsInRoleAsync(user, adminRole))
            await userManager.AddToRoleAsync(user, adminRole);
    }
}
