using System.ComponentModel.DataAnnotations;

namespace GetThereShared.Contracts;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(12)] string Password,
    [Required] string FullName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record RefreshTokenRequest(
    [Required] string RefreshToken);

public class LoginResponse
{
    [Required] public UserResponse User { get; set; } = null!;
    [Required] public string AccessToken { get; set; } = string.Empty;
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenResponse
{
    [Required] public string AccessToken { get; set; } = string.Empty;
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

public class UserResponse
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

public record UpdateProfileRequest
{
    public string? FullName { get; set; }
    [EmailAddress] public string? Email { get; set; }
}
