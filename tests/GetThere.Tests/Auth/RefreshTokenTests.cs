using GetThereAPI.Data;
using GetThereAPI.Exceptions;
using GetThereAPI.Managers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using GetThereShared.Contracts;

namespace GetThere.Tests.Auth;

/// <summary>
/// Refresh-token rotation and reuse detection.
/// <para>
/// Origin: reuse detection was unreachable. Rotation sets both <c>RevokedAt</c> and
/// <c>ReplacedByToken</c>, and the <c>IsActive</c> guard ran first — so a replayed token was rejected
/// as merely expired and the family was never revoked. A stolen refresh token went undetected.
/// </para>
/// </summary>
[Collection(AuthCollection.Name)]
public class RefreshTokenTests
{
    private readonly AuthFixture _fixture;

    public RefreshTokenTests(AuthFixture fixture) => _fixture = fixture;

    private static string NewEmail() => $"refresh-{Guid.NewGuid():N}@example.com";
    private const string Password = "Auditor!Probe#2026x";

    private async Task<LoginResponse> LoginAsync(string email, string? ip = "203.0.113.10")
    {
        using var scope = _fixture.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthManager>();
        return await auth.LoginAsync(new LoginRequest(email, Password), false, "probe", ip);
    }

    private async Task<RefreshTokenResponse> RefreshAsync(string token, string? ip = "203.0.113.10")
    {
        using var scope = _fixture.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthManager>();
        return await auth.RefreshAsync(token, "probe", ip);
    }

    private async Task<int> ActiveTokenCountAsync(string userId)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
        return tokens.Count(t => t.IsActive);
    }

    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        var email = NewEmail();
        await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email);

        var refreshed = await RefreshAsync(login.RefreshToken);

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
    }

    [Fact]
    public async Task Replaying_a_rotated_token_revokes_the_whole_family()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email);

        // Legitimate rotation.
        var refreshed = await RefreshAsync(login.RefreshToken);
        Assert.Equal(1, await ActiveTokenCountAsync(userId));

        // The attacker replays the token that was already exchanged.
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(login.RefreshToken));

        // Everything is revoked — including the token the legitimate client is holding.
        Assert.Equal(0, await ActiveTokenCountAsync(userId));

        // ...so the legitimate client's next refresh fails too, forcing a fresh sign-in.
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(refreshed.RefreshToken));
    }

    [Fact]
    public async Task Reuse_is_recorded_in_the_audit_log()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email);

        await RefreshAsync(login.RefreshToken);
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(login.RefreshToken));

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId).Select(a => a.Action).ToListAsync();

        Assert.Contains("RefreshTokenReuseDetected", actions);
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync("not-a-real-token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_token_is_rejected(string token)
    {
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(token));
    }

    // ── The address no longer decides the verdict ────────────────────────────────
    // The first two used to assert the opposite: a refresh from a new address threw, which surfaces as a
    // 401, which the MAUI client answers by clearing its credentials. That fired on every
    // wifi-to-cellular handover — a silent sign-out for a travel app whose users are, by definition,
    // moving — and as deployed it was bypassable anyway, because UseForwardedHeaders() runs with
    // KnownProxies cleared and so honours X-Forwarded-For from any peer. Rotation plus reuse
    // detection is the control that actually catches a stolen token; the tests below it prove that
    // still bites. Inverted rather than deleted, because a silently removed test is how a control
    // comes back by accident.

    [Fact]
    public async Task A_caller_presenting_no_address_still_refreshes()
    {
        var email = NewEmail();
        await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email, ip: "203.0.113.10");

        var refreshed = await RefreshAsync(login.RefreshToken, ip: null);

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task A_caller_from_a_different_address_still_refreshes_and_is_audited()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email, ip: "203.0.113.10");

        // The wifi-to-cellular case: the session has to survive it.
        var refreshed = await RefreshAsync(login.RefreshToken, ip: "198.51.100.7");
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        // The address is still recorded, just as forensics rather than as a verdict.
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId).Select(a => a.Action).ToListAsync();

        Assert.Contains("RefreshAddressChanged", actions);
    }

    [Fact]
    public async Task A_refresh_from_the_same_address_is_not_audited_as_a_change()
    {
        // The other half: the audit line only means something if it is not written every time.
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email, ip: "203.0.113.10");

        await RefreshAsync(login.RefreshToken, ip: "203.0.113.10");

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId).Select(a => a.Action).ToListAsync();

        Assert.DoesNotContain("RefreshAddressChanged", actions);
    }

    [Fact]
    public async Task Theft_is_still_caught_when_the_replay_comes_from_the_issuing_address()
    {
        // The regression that matters most after relaxing the address check: an attacker who has
        // both the token and the original address must still trip reuse detection.
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email, ip: "203.0.113.10");

        await RefreshAsync(login.RefreshToken, ip: "203.0.113.10");
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(login.RefreshToken, ip: "203.0.113.10"));

        Assert.Equal(0, await ActiveTokenCountAsync(userId));
    }

    [Fact]
    public async Task Logout_revokes_the_token()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        var login = await LoginAsync(email);

        using (var scope = _fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthManager>().LogoutAsync(login.RefreshToken);
        }

        Assert.Equal(0, await ActiveTokenCountAsync(userId));
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(login.RefreshToken));
    }

    [Fact]
    public async Task Changing_the_password_revokes_every_session()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);

        // Two devices signed in.
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);
        Assert.Equal(2, await ActiveTokenCountAsync(userId));

        using (var scope = _fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthManager>()
                .ChangePasswordAsync(userId, Password, "Rotated!Probe#2026y");
        }

        Assert.Equal(0, await ActiveTokenCountAsync(userId));
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(first.RefreshToken));
        await Assert.ThrowsAsync<AppException>(() => RefreshAsync(second.RefreshToken));
    }

    [Fact]
    public async Task Registering_an_address_that_already_exists_looks_like_success()
    {
        // Origin: this returned 409 EMAIL_ALREADY_IN_USE, making registration an account oracle.
        var email = NewEmail();
        using var scope = _fixture.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthManager>();

        await auth.RegisterAsync(new RegisterRequest(email, Password, "First"));

        var replay = await Record.ExceptionAsync(() =>
            auth.RegisterAsync(new RegisterRequest(email, Password, "Second")));

        Assert.Null(replay);

        // And it did not overwrite the original account.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var matches = await db.Users.AsNoTracking().Where(u => u.Email == email).ToListAsync();
        Assert.Single(matches);
        Assert.Equal("First", matches[0].FullName);
    }
}
