using FyreApp.Models;
using Microsoft.AspNetCore.Identity;

namespace FyreApp.Services.Techs;

public class TechService : ITechService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public TechService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<TechCreateResult> CreateAsync(TechCreateDto dto)
    {
        // Check if email is already taken
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing != null)
            return new TechCreateResult(TechCreateStatus.EmailAlreadyExists);

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
            return new TechCreateResult(TechCreateStatus.Failed, Errors: createResult.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, "Tech");

        return new TechCreateResult(TechCreateStatus.Success, TechId: user.Id);
    }

    public async Task<TechUpdateResult> UpdateAsync(string id, TechEditDto dto)
    {
         var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new TechUpdateResult(TechUpdateStatus.NotFound);

        // If email is changing, check it isn't taken by someone else
        if (user.Email != dto.Email)
        {
            var existing = await _userManager.FindByEmailAsync(dto.Email);
            if (existing != null && existing.Id != id)
                return new TechUpdateResult(TechUpdateStatus.EmailAlreadyExists);

            user.Email = dto.Email;
            user.UserName = dto.Email;
        }

        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        user.PhoneNumber = dto.PhoneNumber;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return new TechUpdateResult(TechUpdateStatus.Failed, result.Errors.Select(e => e.Description));

        return new TechUpdateResult(TechUpdateStatus.Success);
    }

    public async Task<TechDeactivateResult> DeactivateAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new TechDeactivateResult(TechDeactivateStatus.NotFound);

        user.IsActive = false;

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.UpdateAsync(user);

        return new TechDeactivateResult(TechDeactivateStatus.Success);
    }

    public async Task<TechDeleteResult> DeleteAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return new TechDeleteResult(TechDeleteStatus.NotFound);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return new TechDeleteResult(TechDeleteStatus.Failed);

        return new TechDeleteResult(TechDeleteStatus.Success);
    }

    public async Task<IEnumerable<TechViewModel>> GetAllAsync()
    {
        var users = await _userManager.GetUsersInRoleAsync("Tech");
        return users.Select(u => new TechViewModel
        {
            Id = u.Id,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email ?? string.Empty,
            PhoneNumber = u.PhoneNumber ?? string.Empty,
            IsActive = u.IsActive
        });
    }

    public async Task<TechViewModel?> GetByIdAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;

        // Confirm they're actually a Tech (not just any user)
        if (!await _userManager.IsInRoleAsync(user, "Tech")) return null;

        return new TechViewModel
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            IsActive = user.IsActive
        };
    }
}