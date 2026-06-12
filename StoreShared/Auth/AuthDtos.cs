namespace StoreShared.Auth;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTimeOffset ExpiresAtUtc);

public record CurrentUserDto(int Id, string Username, UserRole Role);

public sealed record UserListItemDto(int Id, string Username, UserRole Role);
