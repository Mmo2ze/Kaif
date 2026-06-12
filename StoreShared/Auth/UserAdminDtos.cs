namespace StoreShared.Auth;

public sealed record UserAdminRowDto(int Id, string Username, UserRole Role, bool IsActive);

public sealed record CreateUserRequest(string Username, string Password, UserRole Role);

public sealed record ResetPasswordRequest(string NewPassword);
