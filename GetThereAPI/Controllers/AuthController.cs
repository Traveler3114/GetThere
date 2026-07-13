using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using GetThereAPI.Managers;
using GetThereShared.Contracts;
using GetThereAPI.Common;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[EnableRateLimiting("Auth")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly AuthManager _authManager;

public AuthController(AuthManager authManager) { _authManager = authManager; }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult> Register(RegisterRequest request, CancellationToken ct = default)
    {
        await _authManager.RegisterAsync(request, ct);
        return Ok(new { message = "USER_REGISTERED" });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request, [FromQuery] bool rememberMe = false, CancellationToken ct = default)
    {
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authManager.LoginAsync(request, rememberMe, deviceInfo, ipAddress, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh(
        RefreshTokenRequest request, CancellationToken ct = default)
    {
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authManager.RefreshAsync(request.RefreshToken, deviceInfo, ipAddress, ct);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout(RefreshTokenRequest request, CancellationToken ct = default)
    {
        await _authManager.LogoutAsync(request.RefreshToken, ct);
        return Ok(new { message = "LOGGED_OUT" });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(
        ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        await _authManager.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);
        return Ok(new { message = "PASSWORD_CHANGED" });
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);