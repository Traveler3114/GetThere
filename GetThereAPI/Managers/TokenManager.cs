using System.Security.Claims;
using System.Text;
using GetThereAPI.Entities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GetThereAPI.Managers;

public class TokenManager
{
    private readonly IConfiguration _config;

    public TokenManager(IConfiguration config)
    {
        // IConfiguration lets us read from appsettings.json
        _config = config;
    }

    public string CreateToken(AppUser user)
    {
        // Turn the secret key string into bytes so it can be used cryptographically
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        // This says "sign the token using HMAC-SHA256 with our key"
        // HMAC-SHA256 is a standard secure algorithm for this
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // When should this token stop being valid?
        var expiry = DateTime.UtcNow.AddMinutes(double.Parse(_config["Jwt:ExpiryMinutes"]!));

        // Claims = facts about the user baked into the token
        // These get decoded on every request — no database lookup needed!
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                // Sub = "subject" = the user's unique ID
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                // Their email
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                // Their name
                new Claim(JwtRegisteredClaimNames.GivenName, user.FullName ?? ""),
                // Jti = a unique ID for this specific token (useful to blacklist later)
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Expires = expiry,
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            SigningCredentials = creds
        };

        // JsonWebTokenHandler is the modern .NET 10 way to create JWTs
        // (faster and preferred over the older JwtSecurityTokenHandler)
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(tokenDescriptor);
        // Returns a string like: eyJhbGci....eyJzdWIi....abc123
    }
}