using System.Text.RegularExpressions;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.UserManagement;

// Keeps FyreApp logins in line with Uptick, the source of truth for who's who: links each login to its
// Uptick user (by email, the first time) and copies that user's name and email onto the login.
public static partial class UptickLoginSync
{
    public sealed record Result(int Linked, int Updated, IReadOnlyList<string> Problems, int TasksAssigned = 0);

    // Uptick names carry a role in brackets, e.g. "Wes Speedon (Field Tech)": dropped, then split at the first space
    public static (string First, string Last) SplitName(string name)
    {
        var bare = BareName(name);
        var space = bare.IndexOf(' ');
        return space < 0 ? (bare, string.Empty) : (bare[..space], bare[(space + 1)..].Trim());
    }

    // "Wes Speedon (Field Tech)" -> "Wes Speedon"
    public static string BareName(string name)
    {
        var bare = RoleSuffix().Replace(name, "").Trim();
        return bare.Length == 0 ? name.Trim() : bare;
    }

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex RoleSuffix();

    // Copies Uptick's name and email onto one login. Returns a problem when the email belongs to another login.
    public static string? Apply(ApplicationUser login, UptickUser uptick, IEnumerable<ApplicationUser> allLogins, out bool changed)
    {
        changed = false;
        var (first, last) = SplitName(uptick.Name);
        if (login.FirstName != first || login.LastName != last)
        {
            login.FirstName = first;
            login.LastName = last;
            changed = true;
        }

        var email = uptick.Email?.Trim();
        if (string.IsNullOrEmpty(email) || string.Equals(login.Email, email, StringComparison.Ordinal)) return null;

        if (allLogins.Any(o => o.Id != login.Id && string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase)))
            return $"{uptick.Name}: Uptick's email {email} belongs to another FyreApp login, so this login keeps {login.Email}.";

        // Identity's default normaliser upper-cases (invariant)
        login.Email = email;
        login.NormalizedEmail = email.ToUpperInvariant();
        login.UserName = email;
        login.NormalizedUserName = email.ToUpperInvariant();
        changed = true;
        return null;
    }

    // Links unlinked logins to the Uptick user with the same email (a staff one when several share it), then
    // refreshes every linked login from Uptick. Doesn't save.
    public static async Task<Result> ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        var logins = await db.Users.ToListAsync(ct);
        var uptick = (await db.UptickUsers.ToListAsync(ct)).ToDictionary(u => u.Id);
        var problems = new List<string>();
        int linked = 0, updated = 0;

        var taken = logins.Where(l => l.UptickUserId != null).Select(l => l.UptickUserId!.Value).ToHashSet();
        var byEmail = uptick.Values
            .Where(u => !string.IsNullOrWhiteSpace(u.Email) && !taken.Contains(u.Id))
            .GroupBy(u => u.Email!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() == 1 ? g.Single() : g.Where(u => u.LicenseType != "CUSTOMER").ToList() is [var staff] ? staff : null)
            .OfType<UptickUser>()
            .ToDictionary(u => u.Email!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var login in logins.Where(l => l.UptickUserId == null && l.Email != null))
        {
            if (!byEmail.Remove(login.Email!.Trim(), out var match)) continue;
            login.UptickUserId = match.Id;
            linked++;
        }

        foreach (var login in logins.Where(l => l.UptickUserId != null))
        {
            if (!uptick.TryGetValue(login.UptickUserId!.Value, out var u)) continue;
            if (Apply(login, u, logins, out var changed) is { } problem) problems.Add(problem);
            if (changed) updated++;
        }

        return new Result(linked, updated, problems);
    }
}
