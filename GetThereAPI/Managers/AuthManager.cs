using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;
using GetThereAPI.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class AuthManager
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly TokenManager _tokenManager;
    private readonly AppDbContext _db;

    public AuthManager(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, TokenManager tokenManager, AppDbContext db) { _userManager = userManager; _signInManager = signInManager; _tokenManager = tokenManager; _db = db; }

    private void LogAudit(string userId, string action, string entityType = "User", string entityId = "", string? oldValues = null, string? newValues = null)
    {
        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
            throw new AppException("Email already in use", 409, "EMAIL_ALREADY_IN_USE");

        var user = new AppUser { Email = request.Email, UserName = request.Email, FullName = request.FullName };
        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, RoleNames.User);

        LogAudit(user.Id, "Register", "User", user.Id);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, bool rememberMe, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            throw new AppException("Invalid credentials.", 401, "INVALID_CREDENTIALS");

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, true);
        if (!signInResult.Succeeded)
        {
            LogAudit(user.Id, "LoginFailed", "User", user.Id);
            await _db.SaveChangesAsync(ct);
            throw new AppException("Invalid credentials.", 401, "INVALID_CREDENTIALS");
        }

        var accessToken = await _tokenManager.CreateTokenAsync(user);
        var rawRefreshToken = _tokenManager.GenerateRefreshToken();
        var refreshTokenHash = _tokenManager.HashToken(rawRefreshToken);
        var refreshTokenExpiry = _tokenManager.GetRefreshTokenExpiry(rememberMe);

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenHash,
            UserId = user.Id,
            ExpiresAt = refreshTokenExpiry,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        };

        _db.RefreshTokens.Add(refreshToken);

        LogAudit(user.Id, "Login", "User", user.Id, newValues: $"RememberMe:{rememberMe}");
        await _db.SaveChangesAsync(ct);

        return new LoginResponse
        {
            User = AuthMapper.ToResponse(user),
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken
        };
    }

    public async Task<RefreshTokenResponse> RefreshAsync(string rawRefreshToken, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            throw new AppException("Invalid refresh token.", 401, "INVALID_REFRESH_TOKEN");

        var incomingTokenHash = _tokenManager.HashToken(rawRefreshToken);
        var existingRefreshToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == incomingTokenHash, ct);

        if (existingRefreshToken is null || !existingRefreshToken.IsActive)
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");

        if (existingRefreshToken.IpAddress is not null && ipAddress is not null &&
            existingRefreshToken.IpAddress != ipAddress)
        {
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");
        }

        // Token reuse detection — if this token was already replaced, revoke all tokens for this user
        if (existingRefreshToken.ReplacedByToken is not null)
        {
            var userTokens = await _db.RefreshTokens
                .Where(rt => rt.UserId == existingRefreshToken.UserId && rt.IsActive)
                .ToListAsync(ct);
            foreach (var t in userTokens)
                t.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");
        }

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
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        };

        existingRefreshToken.ReplacedByToken = newHashedRefreshToken;

        _db.RefreshTokens.Add(newRefreshTokenEntity);

        LogAudit(existingRefreshToken.UserId, "TokenRefresh", "RefreshToken", existingRefreshToken.Id.ToString());
        await _db.SaveChangesAsync(ct);

        var newAccessToken = await _tokenManager.CreateTokenAsync(existingRefreshToken.User);

        return new RefreshTokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRawRefreshToken
        };
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var tokenHash = _tokenManager.HashToken(rawRefreshToken);
            var existingRefreshToken = await _db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == tokenHash, ct);

            if (existingRefreshToken is not null && !existingRefreshToken.RevokedAt.HasValue)
            {
                existingRefreshToken.RevokedAt = DateTime.UtcNow;
                LogAudit(existingRefreshToken.UserId, "Logout", "RefreshToken", existingRefreshToken.Id.ToString());
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    public async Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            throw new AppException("User not found", 404, "USER_NOT_FOUND");

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)));

        LogAudit(userId, "PasswordChanged", "User", userId);
        await _db.SaveChangesAsync(ct);

        // Revoke all active refresh tokens
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.IsActive)
            .ToListAsync(ct);
        foreach (var t in activeTokens)
            t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
