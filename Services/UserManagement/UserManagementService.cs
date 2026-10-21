using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.UserManagement;

// FyreApp logins. Uptick is the source of truth for who's who: a login is given to an Uptick user and takes
// its name and email from them (UptickLoginSync); FyreApp only adds the role, phone, password and active flag.
public class UserManagementService : IUserManagementService
{
    // Every role the app uses; created at startup if missing (Program.cs)
    public static readonly string[] Roles = ["Admin", "Tech", "Developer"];
    private static readonly string[] AllowedRoles = Roles;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public UserManagementService(UserManager<ApplicationUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public IReadOnlyList<string> GetAvailableRoles() => AllowedRoles;

    public async Task<IReadOnlyList<UserViewModel>> GetUnlinkedAsync()
    {
        var users = await _userManager.Users
            .Where(u => u.UptickUserId == null)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        var result = new List<UserViewModel>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(Map(u, roles.FirstOrDefault() ?? string.Empty, null));
        }
        return result;
    }

    public async Task<IReadOnlyList<UptickUser>> GetLinkableUptickUsersAsync() =>
        await _db.UptickUsers.AsNoTracking()
            .Where(u => u.LicenseType != "CUSTOMER" && u.LicenseType != "API" && !_db.Users.Any(a => a.UptickUserId == u.Id))
            .OrderBy(u => u.Name)
            .ToListAsync();

    public Task<UptickUser?> GetUptickUserAsync(int id) =>
        _db.UptickUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

    public async Task<UserViewModel?> GetByIdAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        var uptickName = user.UptickUserId == null ? null
            : await _db.UptickUsers.Where(u => u.Id == user.UptickUserId).Select(u => u.Name).FirstOrDefaultAsync();
        return Map(user, roles.FirstOrDefault() ?? string.Empty, uptickName);
    }

    public async Task<UserCreateResult> CreateAsync(UserCreateDto dto)
    {
        if (!AllowedRoles.Contains(dto.Role))
            return new UserCreateResult(UserCreateStatus.InvalidRole);

        var uptick = await _db.UptickUsers.FindAsync(dto.UptickUserId);
        if (uptick == null)
            return new UserCreateResult(UserCreateStatus.UptickUserNotFound);
        if (string.IsNullOrWhiteSpace(uptick.Email))
            return new UserCreateResult(UserCreateStatus.NoEmail);
        if (await _db.Users.AnyAsync(u => u.UptickUserId == uptick.Id))
            return new UserCreateResult(UserCreateStatus.AlreadyHasLogin);

        var email = uptick.Email.Trim();
        if (await _userManager.FindByEmailAsync(email) != null)
            return new UserCreateResult(UserCreateStatus.EmailAlreadyExists);

        var (first, last) = UptickLoginSync.SplitName(uptick.Name);
        var user = new ApplicationUser
        {
            FirstName = first,
            LastName = last,
            Email = email,
            UserName = email,
            PhoneNumber = dto.PhoneNumber,
            IsActive = true,
            UptickUserId = uptick.Id
        };

        // No password yet when they're emailed a link to choose one
        var createResult = string.IsNullOrEmpty(dto.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
            return new UserCreateResult(UserCreateStatus.Failed, Errors: createResult.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, dto.Role);

        return new UserCreateResult(UserCreateStatus.Success, UserId: user.Id);
    }

    // A linked login only changes its phone and role here; its name and email come from Uptick
    public async Task<UserUpdateResult> UpdateAsync(string id, UserEditDto dto)
    {
        if (!AllowedRoles.Contains(dto.Role))
            return new UserUpdateResult(UserUpdateStatus.InvalidRole);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new UserUpdateResult(UserUpdateStatus.NotFound);

        if (user.UptickUserId == null)
        {
            if (user.Email != dto.Email)
            {
                var existing = await _userManager.FindByEmailAsync(dto.Email);
                if (existing != null && existing.Id != id)
                    return new UserUpdateResult(UserUpdateStatus.EmailAlreadyExists);

                user.Email = dto.Email;
                user.UserName = dto.Email;
            }

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
        }
        user.PhoneNumber = dto.PhoneNumber;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return new UserUpdateResult(UserUpdateStatus.Failed, updateResult.Errors.Select(e => e.Description));

        var currentRoles = await _userManager.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault();
        if (currentRole != dto.Role)
        {
            if (currentRole != null)
                await _userManager.RemoveFromRoleAsync(user, currentRole);
            await _userManager.AddToRoleAsync(user, dto.Role);
        }

        return new UserUpdateResult(UserUpdateStatus.Success);
    }

    // Links a login made before Uptick was imported to its Uptick user, which then sets its name and email
    public async Task<UserLinkResult> LinkAsync(string id, int uptickUserId)
    {
        var logins = await _db.Users.ToListAsync();
        var user = logins.FirstOrDefault(u => u.Id == id);
        if (user == null)
            return new UserLinkResult(UserLinkStatus.NotFound);

        var uptick = await _db.UptickUsers.FindAsync(uptickUserId);
        if (uptick == null)
            return new UserLinkResult(UserLinkStatus.UptickUserNotFound);
        if (logins.Any(u => u.UptickUserId == uptickUserId && u.Id != id))
            return new UserLinkResult(UserLinkStatus.AlreadyHasLogin);

        user.UptickUserId = uptick.Id;
        var problem = UptickLoginSync.Apply(user, uptick, logins, out _);
        await _db.SaveChangesAsync();
        return new UserLinkResult(UserLinkStatus.Success, problem);
    }

    public async Task<UptickLoginSync.Result> SyncFromUptickAsync()
    {
        var result = await UptickLoginSync.ApplyAsync(_db);
        var tasksAssigned = await FyreApp.Services.Tasks.TechnicianMatch.AssignImportedTasksAsync(_db);
        await _db.SaveChangesAsync();
        return result with { TasksAssigned = tasksAssigned };
    }

    public async Task<UserDeactivateResult> DeactivateAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new UserDeactivateResult(UserDeactivateStatus.NotFound);

        user.IsActive = false;
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.UpdateAsync(user);

        return new UserDeactivateResult(UserDeactivateStatus.Success);
    }

    public async Task<UserActivateResult> ActivateAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new UserActivateResult(UserActivateStatus.NotFound);

        user.IsActive = true;
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.UpdateAsync(user);

        return new UserActivateResult(UserActivateStatus.Success);
    }

    public async Task<UserDeleteResult> DeleteAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new UserDeleteResult(UserDeleteStatus.NotFound);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return new UserDeleteResult(UserDeleteStatus.Failed);

        return new UserDeleteResult(UserDeleteStatus.Success);
    }

    public async Task<UserResetPasswordResult> ResetPasswordAsync(string id, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new UserResetPasswordResult(UserResetPasswordStatus.NotFound);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return new UserResetPasswordResult(UserResetPasswordStatus.Failed, result.Errors.Select(e => e.Description));

        return new UserResetPasswordResult(UserResetPasswordStatus.Success);
    }

    private static UserViewModel Map(ApplicationUser u, string role, string? uptickName) => new()
    {
        Id = u.Id,
        FirstName = u.FirstName,
        LastName = u.LastName,
        Email = u.Email ?? string.Empty,
        PhoneNumber = u.PhoneNumber ?? string.Empty,
        Role = role,
        IsActive = u.IsActive,
        UptickUserId = u.UptickUserId,
        UptickName = uptickName
    };
}
