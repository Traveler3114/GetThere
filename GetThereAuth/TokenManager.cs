using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GetThereAuth;

/// <summary>
/// Issues access tokens and the raw/hashed refresh-token pairs that go with them.
/// <para>
/// This existed as two near-identical copies, one per API, which is how the two drifted: a change
/// to token contents or lifetime had to be made twice and silently applied to one. It is generic
/// over the user type so each API keeps its own <c>AppUser</c> entity and EF model.
/// </para>
/// </summary>
public class TokenManager<TUser> where TUser : IdentityUser, IAuthUser
{
    private readonly IConfiguration _config;
    private readonly UserManager<TUser> _userManager;

    public TokenManager(IConfiguration config, UserManager<TUser> userManager)
    {
        _config = config;
        _userManager = userManager;
    }

    public async Task<string> CreateTokenAsync(TUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiry = DateTime.UtcNow.AddMinutes(
            double.TryParse(_config["Jwt:ExpiryMinutes"], out var expiryMin) ? expiryMin : 60);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.GivenName, user.FullName ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("nbf", EpochTime.GetIntDate(DateTime.UtcNow).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
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

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

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
