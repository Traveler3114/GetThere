using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;
using GetThereAPI.Mapping;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly TokenManager _tokenManager;
    private readonly AppDbContext _dbContext;

    public AuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        TokenManager tokenManager,
        AppDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenManager = tokenManager;
        _dbContext = dbContext;
    }

    [HttpPost("register")]
    public async Task<ActionResult<OperationResult>> Register(RegisterRequest request, CancellationToken ct = default)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
            return BadRequest(OperationResult.Fail("EMAIL_ALREADY_IN_USE", "Email already in use"));

        var user = new AppUser { Email = request.Email, UserName = request.Email, FullName = request.FullName };
        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            return BadRequest(OperationResult.Fail(string.Join(", ", result.Errors.Select(e => e.Description))));

        return Ok(OperationResult.Ok("USER_REGISTERED"));
    }

    [HttpPost("login")]
    public async Task<ActionResult<OperationResult<LoginResponse>>> Login(LoginRequest request, [FromQuery] bool rememberMe = false, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Unauthorized(OperationResult<LoginResponse>.Fail("INVALID_CREDENTIALS", "Invalid credentials."));

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded)
            return Unauthorized(OperationResult<LoginResponse>.Fail("INVALID_CREDENTIALS", "Invalid credentials."));

        var accessToken = _tokenManager.CreateToken(user);
        var rawRefreshToken = _tokenManager.GenerateRefreshToken();
        var refreshTokenHash = _tokenManager.HashToken(rawRefreshToken);
        var refreshTokenExpiry = _tokenManager.GetRefreshTokenExpiry(rememberMe);

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenHash,
            UserId = user.Id,
            ExpiresAt = refreshTokenExpiry,
            DeviceInfo = Request.Headers["User-Agent"].ToString()
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(ct);

        var userDto = AuthMapper.ToResponse(user, accessToken);

        return Ok(OperationResult<LoginResponse>.Ok(new LoginResponse
        {
            User = userDto,
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken
        }));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<OperationResult<RefreshTokenResponse>>> Refresh(RefreshTokenRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unauthorized(OperationResult<RefreshTokenResponse>.Fail("INVALID_REFRESH_TOKEN", "Invalid refresh token."));

        var incomingTokenHash = _tokenManager.HashToken(request.RefreshToken);
        var existingRefreshToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == incomingTokenHash, ct);

        if (existingRefreshToken is null || !existingRefreshToken.IsActive)
            return Unauthorized(OperationResult<RefreshTokenResponse>.Fail("REFRESH_TOKEN_EXPIRED", "Refresh token is invalid or expired."));

        existingRefreshToken.RevokedAt = DateTime.UtcNow;

        var newRawRefreshToken = _tokenManager.GenerateRefreshToken();
        var newHashedRefreshToken = _tokenManager.HashToken(newRawRefreshToken);
        var wasRememberMeToken = _tokenManager.IsRememberMeRefreshToken(
            existingRefreshToken.CreatedAt,
            existingRefreshToken.ExpiresAt);

        var newRefreshTokenEntity = new RefreshToken
        {
            Token = newHashedRefreshToken,
            UserId = existingRefreshToken.UserId,
            ExpiresAt = _tokenManager.GetRefreshTokenExpiry(wasRememberMeToken),
            DeviceInfo = Request.Headers["User-Agent"].ToString()
        };

        existingRefreshToken.ReplacedByToken = newHashedRefreshToken;

        _dbContext.RefreshTokens.Add(newRefreshTokenEntity);
        await _dbContext.SaveChangesAsync(ct);

        var newAccessToken = _tokenManager.CreateToken(existingRefreshToken.User);

        return Ok(OperationResult<RefreshTokenResponse>.Ok(new RefreshTokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRawRefreshToken
        }));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<OperationResult>> Logout(RefreshTokenRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var tokenHash = _tokenManager.HashToken(request.RefreshToken);
            var existingRefreshToken = await _dbContext.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == tokenHash, ct);

            if (existingRefreshToken is not null && !existingRefreshToken.RevokedAt.HasValue)
            {
                existingRefreshToken.RevokedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        return Ok(OperationResult.Ok("LOGGED_OUT"));
    }
}
