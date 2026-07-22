using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

public class TokenManager
{
    private readonly IConfiguration _config;
    private readonly UserManager<AppUser> _userManager;

    public TokenManager(IConfiguration config, UserManager<AppUser> userManager)
    {
        _config = config;
        _userManager = userManager;
    }

    public async Task<string> CreateTokenAsync(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = double.TryParse(_config["Jwt:ExpiryMinutes"], out var m) ? m : 60;
        var expiry = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, user.Email!),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.GivenName, user.FullName ?? ""),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("nbf", EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64)
        };

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
            claims.Add(new Claim("role", role));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry,
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            SigningCredentials = creds
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(tokenDescriptor);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }

    public string HashToken(string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    public DateTime GetRefreshTokenExpiry(bool rememberMe)
    {
        var days = rememberMe
            ? (int.TryParse(_config["Jwt:RefreshTokenDaysRememberMe"], out var remDays) ? remDays : 30)
            : (int.TryParse(_config["Jwt:RefreshTokenDays"], out var stdDays) ? stdDays : 1);
        return DateTime.UtcNow.AddDays(days);
    }

    public bool IsRememberMeRefreshToken(DateTime createdAt, DateTime expiresAt)
    {
        var standardDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var days) ? days : 1;
        return (expiresAt - createdAt) > TimeSpan.FromDays(standardDays);
    }
}