using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting("Auth")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly AuthManager _authManager;

    public AuthController(AuthManager authManager) { _authManager = authManager; }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        [FromQuery] bool rememberMe = false,
        CancellationToken ct = default)
    {
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authManager.LoginAsync(request, rememberMe, deviceInfo, ipAddress, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct = default)
    {
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authManager.RefreshAsync(request.RefreshToken, deviceInfo, ipAddress, ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult> Logout(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct = default)
    {
        await _authManager.LogoutAsync(request.RefreshToken, ct);
        return Ok(new { message = "LOGGED_OUT" });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        await _authManager.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);
        return Ok(new { message = "PASSWORD_CHANGED" });
    }

    [HttpPost("register")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<ActionResult> Register(
        [FromBody] CreateUserRequest request,
        CancellationToken ct = default)
    {
        await _authManager.RegisterAsync(request, ct);
        return Ok(new { message = "USER_CREATED" });
    }
}