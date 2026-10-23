using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Tasks;

// Tasks are assigned to Uptick users (the people on the Users page). Who can be picked: anyone whose FyreApp login is a
// technician (the Tech role), not Uptick's licence type. And matching the technician Uptick names on an imported task
// ("Assigned Technician", e.g. "Dwayne Page") to the Uptick user of that name.
public static class TechnicianMatch
{
    public const string TechRole = "Tech";

    // People with an active FyreApp login in the Tech role
    public static IQueryable<UptickUser> Assignable(this IQueryable<UptickUser> users, AppDbContext db) =>
        users.Where(u => db.Users.Any(l => l.UptickUserId == u.Id && l.IsActive
            && db.UserRoles.Any(ur => ur.UserId == l.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == TechRole))));

    public static Task<bool> IsAssignableAsync(AppDbContext db, int uptickUserId, CancellationToken ct = default) =>
        db.UptickUsers.Where(u => u.Id == uptickUserId).Assignable(db).AnyAsync(ct);

    // Uptick user ids by name, role suffix dropped ("Wes Speedon (Field Tech)" -> "Wes Speedon").
    // Names shared by more than one user are left out rather than guessed.
    public static async Task<Dictionary<string, int>> IdsByNameAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.UptickUsers.AsNoTracking().Where(u => u.LicenseType != "CUSTOMER").Select(u => new { u.Id, u.Name }).ToListAsync(ct))
        .GroupBy(u => UptickLoginSync.BareName(u.Name), StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() == 1)
        .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);

    public static int? Find(IReadOnlyDictionary<string, int> idsByName, string? uptickTechnician) =>
        uptickTechnician != null && idsByName.TryGetValue(UptickLoginSync.BareName(uptickTechnician), out var id) ? id : null;

    // Imported tasks Uptick has assigned to someone, not yet assigned in FyreApp: assigns the Uptick user of
    // that name (e.g. after importing the Users export following the Tasks export). Doesn't save.
    public static async Task<int> AssignImportedTasksAsync(AppDbContext db, CancellationToken ct = default)
    {
        var idsByName = await IdsByNameAsync(db, ct);
        if (idsByName.Count == 0) return 0;

        var assigned = 0;
        foreach (var task in await db.ClientTasks.Where(t => t.AssignedToUptickUserId == null && t.UptickTechnician != null).ToListAsync(ct))
        {
            if (Find(idsByName, task.UptickTechnician) is not int id) continue;
            task.AssignedToUptickUserId = id;
            assigned++;
        }
        return assigned;
    }
}
