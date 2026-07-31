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
    private readonly ILogger<AuthManager> _logger;

    public AuthManager(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, TokenManager tokenManager, AppDbContext db, ILogger<AuthManager> logger) { _userManager = userManager; _signInManager = signInManager; _tokenManager = tokenManager; _db = db; _logger = logger; }

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
            SharedAuth.AccountEnumerationGuard.SpendPasswordHashingTime(_userManager, request.Password);
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

        // Checked, not discarded. An account that exists with no role holds no permissions at all,
        // so every subsequent request from it is a 403 the user cannot explain or recover from.
        var roleResult = await _userManager.AddToRoleAsync(user, RoleNames.User);
        if (!roleResult.Succeeded)
            throw new AppException(string.Join(", ", roleResult.Errors.Select(e => e.Description)));

        // Created with the account rather than on first read. Registration used to leave the user
        // without one, so anything reaching the wallet before the client happened to call
        // POST /wallet/ensure answered 404 — including a purchase, which fails with
        // WALLET_NOT_FOUND. The endpoint stays as it is for accounts that predate this.
        _db.Wallets.Add(new Wallet { UserId = user.Id });

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
            SharedAuth.AccountEnumerationGuard.SpendPasswordHashingTime(_userManager, request.Password);
            throw new AppException("Invalid credentials.", 401, "INVALID_CREDENTIALS");
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, true);
        if (!signInResult.Succeeded)
        {
            LogAudit(user.Id, "LoginFailed", "User", user.Id);
            await _db.SaveChangesAsync(ct);
            throw new AppException("Invalid credentials.", 401, "INVALID_CREDENTIALS");
        }

        // Recorded here rather than left unset: the admin console renders this column
        // (AdminManager.GetUsersAsync, RolePermissionManager) and it was blank for every user in
        // this service, because only TransitInfoAPI's AuthManager ever wrote it.
        user.LastLogin = DateTime.UtcNow;

        // Not fatal to the login if it fails — the credentials were already accepted — but silently
        // discarding the result is how this column ends up stale again, which is the defect the
        // write was added to fix.
        var lastLoginResult = await _userManager.UpdateAsync(user);
        if (!lastLoginResult.Succeeded)
        {
            _logger.LogWarning("Could not record LastLogin for user {UserId}: {Errors}",
                user.Id, string.Join("; ", lastLoginResult.Errors.Select(e => e.Description)));
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

        // The rules — including why reuse must be tested before the active check — live in
        // SharedAuth so this API and TransitInfoAPI cannot disagree about what counts as theft.
        var verdict = SharedAuth.RefreshTokenEvaluator.Evaluate(
            found: existingRefreshToken is not null,
            hasReplacement: existingRefreshToken?.ReplacedByToken is not null,
            isActive: existingRefreshToken?.IsActive ?? false);

        if (existingRefreshToken is null || verdict is SharedAuth.RefreshTokenVerdict.Invalid)
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");

        // Reuse means the token was already rotated once and has been presented again: assume it
        // was stolen and revoke every live token the user has.
        if (verdict is SharedAuth.RefreshTokenVerdict.ReuseDetected)
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

        // The token itself is good, but the account behind it may have changed since it was issued.
        // Nothing rechecked that, so an account locked out by the failed-attempt policy kept minting
        // fresh access tokens for the whole life of its refresh token — the lockout only ever
        // applied to the password path.
        var account = existingRefreshToken.User;
        if (account is null || await _userManager.IsLockedOutAsync(account))
        {
            LogAudit(existingRefreshToken.UserId, "RefreshRejectedForLockedAccount", "RefreshToken",
                existingRefreshToken.Id.ToString(CultureInfo.InvariantCulture));
            await _db.SaveChangesAsync(ct);

            // Deliberately the same answer as an expired token: whether an account exists and is
            // locked is not something an unauthenticated caller should be able to tell apart.
            throw new AppException("Refresh token is invalid or expired.", 401, "REFRESH_TOKEN_EXPIRED");
        }

        // Recorded, not enforced. An address change no longer rejects the token — see
        // RefreshTokenEvaluator.IsAddressChange for why — but it is still the signal you would want
        // when investigating an account, so it goes in the audit log rather than being discarded.
        if (SharedAuth.RefreshTokenEvaluator.IsAddressChange(existingRefreshToken.IpAddress, ipAddress))
        {
            LogAudit(existingRefreshToken.UserId, "RefreshAddressChanged", "RefreshToken",
                existingRefreshToken.Id.ToString(CultureInfo.InvariantCulture),
                oldValues: existingRefreshToken.IpAddress, newValues: ipAddress);
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
