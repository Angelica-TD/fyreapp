using System.ComponentModel.DataAnnotations;
using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.UserManagement;

// A person added in FyreApp rather than brought in from Uptick: a technician to test the field app with, say
public sealed class PersonInput
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    // Trade licence, e.g. "QBCC 15366855"
    [StringLength(200)]
    public string? Licence { get; set; }

    // After adding, go on to give them a FyreApp login
    public bool GiveLogin { get; set; } = true;
}

public enum PersonStatus { Done, NotFound, FromUptick, EmailTaken, Invalid }
public sealed record PersonResult(PersonStatus Status, int? Id = null, string? Message = null);

// What deleting a person takes with it, for the confirmation page
public sealed record PersonDeleteInfo(UptickUser Person, string? LoginId, string? LoginEmail, string? LoginRole, int TasksAssigned, int WorkRecorded);

public interface IPeopleService
{
    Task<UptickUser?> GetAsync(int id, CancellationToken ct = default);
    Task<PersonResult> CreateAsync(PersonInput input, CancellationToken ct = default);

    // Also brings their login's name and email up to date
    Task<PersonResult> UpdateAsync(int id, PersonInput input, CancellationToken ct = default);

    Task<PersonDeleteInfo?> DeleteInfoAsync(int id, CancellationToken ct = default);

    // Deletes the person and their login. Tasks assigned to them become unassigned; work they recorded stays, without
    // their name.
    Task<PersonResult> DeleteAsync(int id, CancellationToken ct = default);
}

// The people on the Users page (UptickUser) that were added in FyreApp (no Uptick id). Those from Uptick are Uptick's:
// changed there, brought in by the migration.
public class PeopleService : IPeopleService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _logins;

    public PeopleService(AppDbContext db, UserManager<ApplicationUser> logins)
    {
        _db = db;
        _logins = logins;
    }

    public Task<UptickUser?> GetAsync(int id, CancellationToken ct = default) =>
        _db.UptickUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<PersonResult> CreateAsync(PersonInput input, CancellationToken ct = default)
    {
        if (await ProblemAsync(input, null, ct) is { } problem) return problem;
        var person = new UptickUser { JoinedUtc = DateTime.UtcNow };
        Copy(input, person);
        _db.UptickUsers.Add(person);
        await _db.SaveChangesAsync(ct);
        return new(PersonStatus.Done, person.Id);
    }

    public async Task<PersonResult> UpdateAsync(int id, PersonInput input, CancellationToken ct = default)
    {
        var person = await _db.UptickUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (person == null) return new(PersonStatus.NotFound);
        if (person.UptickId != null) return new(PersonStatus.FromUptick, id, "They come from Uptick: change them there.");
        if (await ProblemAsync(input, id, ct) is { } problem) return problem;
        Copy(input, person);

        string? message = null;
        var login = await _db.Users.FirstOrDefaultAsync(l => l.UptickUserId == id, ct);
        if (login != null)
            message = UptickLoginSync.Apply(login, person, await _db.Users.Where(l => l.Id != login.Id).ToListAsync(ct), out _);
        await _db.SaveChangesAsync(ct);
        return new(PersonStatus.Done, id, message);
    }

    public async Task<PersonDeleteInfo?> DeleteInfoAsync(int id, CancellationToken ct = default)
    {
        var person = await GetAsync(id, ct);
        if (person == null) return null;
        var login = await _db.Users.AsNoTracking().Where(l => l.UptickUserId == id).Select(l => new { l.Id, l.Email }).FirstOrDefaultAsync(ct);
        var role = login == null ? null : await _db.UserRoles.Where(r => r.UserId == login.Id)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name).FirstOrDefaultAsync(ct);
        var work = await _db.TaskSessions.CountAsync(s => s.UptickUserId == id, ct)
                   + await _db.ServiceTasks.CountAsync(s => s.TechnicianId == id, ct)
                   + await _db.TaskStockTransactions.CountAsync(s => s.UptickUserId == id, ct);
        return new PersonDeleteInfo(person, login?.Id, login?.Email, role,
            await _db.ClientTasks.CountAsync(t => t.AssignedToUptickUserId == id, ct), work);
    }

    public async Task<PersonResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var person = await _db.UptickUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (person == null) return new(PersonStatus.NotFound);
        if (person.UptickId != null) return new(PersonStatus.FromUptick, id, "They come from Uptick, so they can't be deleted here.");

        var login = await _logins.Users.FirstOrDefaultAsync(l => l.UptickUserId == id, ct);
        if (login != null)
        {
            var deleted = await _logins.DeleteAsync(login);
            if (!deleted.Succeeded) return new(PersonStatus.Invalid, id, "Their login couldn't be deleted.");
        }

        // What points at them is let go: tasks unassigned, recorded work kept without their name
        // (the database does the same for the rest)
        foreach (var task in await _db.ClientTasks.Where(t => t.AssignedToUptickUserId == id).ToListAsync(ct))
            task.AssignedToUptickUserId = null;
        _db.UptickUsers.Remove(person);
        await _db.SaveChangesAsync(ct);
        return new(PersonStatus.Done, id);
    }

    private async Task<PersonResult?> ProblemAsync(PersonInput input, int? id, CancellationToken ct)
    {
        var email = Clean(input.Email);
        if (email == null) return null;
        // An email Sync would match to someone else's login
        var holder = await _db.UptickUsers.AsNoTracking()
            .Where(u => u.Id != id && u.Email != null && u.Email.ToLower() == email.ToLower())
            .Select(u => u.Name).FirstOrDefaultAsync(ct);
        return holder == null ? null : new(PersonStatus.EmailTaken, id, $"{email} is already {holder}'s email.");
    }

    private static void Copy(PersonInput input, UptickUser person)
    {
        person.Name = input.Name.Trim();
        person.Email = Clean(input.Email);
        person.Licence = Clean(input.Licence);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// Views/Users/Person.cshtml: adding (Id 0) or editing a person
public sealed class PersonPageVm
{
    public int Id { get; set; }
    public PersonInput Input { get; set; } = new();
}
