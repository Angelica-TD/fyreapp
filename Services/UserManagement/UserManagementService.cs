using FyreApp.Models;
using Microsoft.AspNetCore.Identity;

namespace FyreApp.Services.UserManagement;

public class UserManagementService : IUserManagementService
{
    private static readonly string[] AllowedRoles = ["Admin", "Tech", "Developer"];

    private readonly UserManager<ApplicationUser> _userManager;

    public UserManagementService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public IReadOnlyList<string> GetAvailableRoles() => AllowedRoles;

    public async Task<IEnumerable<UserViewModel>> GetAllAsync()
    {
        var users = _userManager.Users
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToList();

        var result = new List<UserViewModel>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(Map(u, roles.FirstOrDefault() ?? string.Empty));
        }
        return result;
    }

    public async Task<UserViewModel?> GetByIdAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return Map(user, roles.FirstOrDefault() ?? string.Empty);
    }

    public async Task<UserCreateResult> CreateAsync(UserCreateDto dto)
    {
        if (!AllowedRoles.Contains(dto.Role))
            return new UserCreateResult(UserCreateStatus.InvalidRole);

        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing != null)
            return new UserCreateResult(UserCreateStatus.EmailAlreadyExists);

        var user = new ApplicationUser
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            UserName = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
            return new UserCreateResult(UserCreateStatus.Failed, Errors: createResult.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, dto.Role);

        return new UserCreateResult(UserCreateStatus.Success, UserId: user.Id);
    }

    public async Task<UserUpdateResult> UpdateAsync(string id, UserEditDto dto)
    {
        if (!AllowedRoles.Contains(dto.Role))
            return new UserUpdateResult(UserUpdateStatus.InvalidRole);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new UserUpdateResult(UserUpdateStatus.NotFound);

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

    private static UserViewModel Map(ApplicationUser u, string role) => new()
    {
        Id = u.Id,
        FirstName = u.FirstName,
        LastName = u.LastName,
        Email = u.Email ?? string.Empty,
        PhoneNumber = u.PhoneNumber ?? string.Empty,
        Role = role,
        IsActive = u.IsActive
    };
}
