namespace TransitInfoAPI.Contracts;

public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record CreateUserRequest(string Email, string Password, string FullName);
public record CreateRoleRequest(string Name, List<string> Permissions);
public record UpdateRolePermissionsRequest(List<string> Permissions);
public record SetRoleRequest(string RoleName);

public class LoginResponse
{
    public UserResponse User { get; set; } = null!;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

