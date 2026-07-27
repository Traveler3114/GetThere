using System.Globalization;

using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;

using GetThereShared.Contracts;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
        {
            // Deliberately indistinguishable from success. Answering "email already in use" turns
            // registration into an oracle for whether an address has an account here. The address
            // owner is told by mail instead — see the note below.
            _userManager.PasswordHasher.HashPassword(new AppUser(), request.Password);
            LogAudit(existingUser.Id, "RegisterAttemptOnExistingAccount", "User", existingUser.Id);
            await _db.SaveChangesAsync(ct);

            // TODO: send a "someone tried to register with your address" mail once an email sender
            // exists. Until then the duplicate attempt is only visible in the audit log.
            return;
        }

        var user = new AppUser { Email = request.Email, UserName = request.Email, FullName = request.FullName };
        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            // Identity reports duplicate-email as a validation error too; collapse it into the same
            // silent success so the race between the check above and this call is not an oracle either.
            if (result.Errors.All(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                return;

            throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, RoleNames.User);

        LogAudit(user.Id, "Register", "User", user.Id);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, bool rememberMe, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Hash the supplied password anyway so an unknown address does not answer measurably
            // faster than a known one, which would make the endpoint an account oracle.
            _userManager.PasswordHasher.HashPassword(new AppUser(), request.Password);
            throw new AppException("Invalid credentials.", 401, "INVALID_CREDENTIALS");
        }

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

        if (existingRefreshToken is null)
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");

        // Token reuse detection — if this token was already replaced, revoke all tokens for this user.
        // This must run *before* the IsActive guard: rotation sets both RevokedAt and ReplacedByToken,
        // so a replayed rotated token is already inactive and would otherwise never reach this branch.
        if (existingRefreshToken.ReplacedByToken is not null)
        {
            var revokedAt = DateTime.UtcNow;
            var userTokens = await _db.RefreshTokens
                .Where(rt => rt.UserId == existingRefreshToken.UserId && rt.RevokedAt == null && rt.ExpiresAt > revokedAt)
                .ToListAsync(ct);
            foreach (var t in userTokens)
                t.RevokedAt = revokedAt;

            LogAudit(existingRefreshToken.UserId, "RefreshTokenReuseDetected", "RefreshToken", existingRefreshToken.Id.ToString(CultureInfo.InvariantCulture));
            await _db.SaveChangesAsync(ct);
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");
        }

        if (!existingRefreshToken.IsActive)
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");

        // IP binding. A caller that presents no address at all is rejected when the token was issued
        // with one — otherwise suppressing the address is enough to skip the check entirely. A token
        // stored without an address (issued before the address was captured) cannot be compared, so
        // it is allowed through.
        if (existingRefreshToken.IpAddress is not null && existingRefreshToken.IpAddress != ipAddress)
        {
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

        LogAudit(existingRefreshToken.UserId, "TokenRefresh", "RefreshToken", existingRefreshToken.Id.ToString(CultureInfo.InvariantCulture));
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
                LogAudit(existingRefreshToken.UserId, "Logout", "RefreshToken", existingRefreshToken.Id.ToString(CultureInfo.InvariantCulture));
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

        // Revoke all active refresh tokens.
        // The predicate is spelled out rather than using RefreshToken.IsActive: that property is
        // computed in C# and unmapped, so EF cannot translate it and the query throws at runtime.
        var revokedAt = DateTime.UtcNow;
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.ExpiresAt > revokedAt)
            .ToListAsync(ct);
        foreach (var t in activeTokens)
            t.RevokedAt = revokedAt;
        await _db.SaveChangesAsync(ct);
    }
}
