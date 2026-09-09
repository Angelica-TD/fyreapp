namespace FyreApp.Services.UserManagement;

public enum UserCreateStatus { Success, EmailAlreadyExists, InvalidRole, Failed }
public record UserCreateResult(UserCreateStatus Status, string? UserId = null, IEnumerable<string>? Errors = null);

public enum UserUpdateStatus { Success, NotFound, EmailAlreadyExists, InvalidRole, Failed }
public record UserUpdateResult(UserUpdateStatus Status, IEnumerable<string>? Errors = null);

public enum UserDeactivateStatus { Success, NotFound }
public record UserDeactivateResult(UserDeactivateStatus Status);

public enum UserActivateStatus { Success, NotFound }
public record UserActivateResult(UserActivateStatus Status);

public enum UserDeleteStatus { Success, NotFound, Failed }
public record UserDeleteResult(UserDeleteStatus Status);
