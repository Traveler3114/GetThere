using System.ComponentModel.DataAnnotations;

namespace GetThereShared.Dtos;

public class LoginDto
{
    [Required, EmailAddress]
    public string Email    { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class RegisterDto
{
    [Required, EmailAddress]
    public string Email    { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    public string? FullName { get; set; }
}

public class UserDto
{
    public string  Id       { get; set; } = string.Empty;
    public string  Email    { get; set; } = string.Empty;
    public string? FullName { get; set; }

    /// <summary>JWT — included in login/register response only.</summary>
    public string? Token    { get; set; }
}
