namespace FyreApp.Services.UserManagement;

public interface IUserManagementService
{
    Task<IEnumerable<UserViewModel>> GetAllAsync();
    Task<UserViewModel?> GetByIdAsync(string id);
    Task<UserCreateResult> CreateAsync(UserCreateDto dto);
    Task<UserUpdateResult> UpdateAsync(string id, UserEditDto dto);
    Task<UserDeactivateResult> DeactivateAsync(string id);
    Task<UserActivateResult> ActivateAsync(string id);
    Task<UserDeleteResult> DeleteAsync(string id);
    Task<UserResetPasswordResult> ResetPasswordAsync(string id, string newPassword);
    IReadOnlyList<string> GetAvailableRoles();
}
