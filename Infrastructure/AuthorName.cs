using System.Security.Claims;
using FyreApp.Data;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Infrastructure;

public static class AuthorName
{
    // The signed-in user's name for Timeline entries ("Wes Speedon"), else their login name
    public static async Task<string?> AuthorNameAsync(this AppDbContext db, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = id == null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        var fullName = account == null ? null : $"{account.FirstName} {account.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? user.Identity?.Name : fullName;
    }
}
