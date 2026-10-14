using FyreApp.Models;

namespace FyreApp.Services.UserManagement;

public interface IUserManagementService
{
    // Logins not linked to an Uptick user (made before Uptick was imported, or whose email isn't in Uptick)
    Task<IReadOnlyList<UserViewModel>> GetUnlinkedAsync();
    // Uptick users who could be given a login or linked to one: staff and contractors without a login
    Task<IReadOnlyList<UptickUser>> GetLinkableUptickUsersAsync();
    Task<UptickUser?> GetUptickUserAsync(int id);
    Task<UserViewModel?> GetByIdAsync(string id);
    Task<UserCreateResult> CreateAsync(UserCreateDto dto);
    Task<UserUpdateResult> UpdateAsync(string id, UserEditDto dto);
    Task<UserLinkResult> LinkAsync(string id, int uptickUserId);
    Task<UptickLoginSync.Result> SyncFromUptickAsync();
    Task<UserDeactivateResult> DeactivateAsync(string id);
    Task<UserActivateResult> ActivateAsync(string id);
    Task<UserDeleteResult> DeleteAsync(string id);
    Task<UserResetPasswordResult> ResetPasswordAsync(string id, string newPassword);
    IReadOnlyList<string> GetAvailableRoles();
}
