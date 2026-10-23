using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Identity;

namespace FyreApp.Tests.Helpers;

// People with a FyreApp login in a role: a technician (Tech) is who tasks can be assigned to
public static class Logins
{
    public static async Task<UptickUser> PersonAsync(AppDbContext db, string name, string? role = "Tech", bool active = true)
    {
        var person = new UptickUser { Name = name };
        db.UptickUsers.Add(person);
        await db.SaveChangesAsync();
        if (role == null) return person;

        var login = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = $"{person.Id}@test", Email = $"{person.Id}@test", UptickUserId = person.Id, IsActive = active };
        var identityRole = db.Roles.Local.FirstOrDefault(r => r.Name == role) ?? db.Roles.FirstOrDefault(r => r.Name == role);
        if (identityRole == null)
        {
            identityRole = new IdentityRole(role) { Id = Guid.NewGuid().ToString() };
            db.Roles.Add(identityRole);
        }
        db.Users.Add(login);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = login.Id, RoleId = identityRole.Id });
        await db.SaveChangesAsync();
        return person;
    }
}
