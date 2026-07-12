using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("auth")]
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
        var result = await _authManager.LoginAsync(request, rememberMe, deviceInfo, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct = default)
    {
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var result = await _authManager.RefreshAsync(request.RefreshToken, deviceInfo, ct);
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
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
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