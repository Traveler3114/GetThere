using SharedAuth;

namespace GetThere.Tests.Auth;

/// <summary>
/// The refresh-token rules, tested directly rather than through either API.
/// <para>
/// These rules existed as two hand-maintained copies, one per API, and the copies had already
/// drifted elsewhere in the auth stack. They are pure decisions with no database involved, so they
/// can be pinned down exhaustively and cheaply — which is the point of having extracted them.
/// </para>
/// </summary>
public class RefreshTokenEvaluatorTests
{
    private const string IssuedIp = "203.0.113.10";

    private const string OtherIp = "198.51.100.7";

    [Fact]
    public void An_unknown_token_is_invalid()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: false, hasReplacement: false, isActive: false);

        Assert.Equal(RefreshTokenVerdict.Invalid, verdict);
    }

    [Fact]
    public void A_live_token_rotates()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: true);

        Assert.Equal(RefreshTokenVerdict.Rotate, verdict);
    }

    [Fact]
    public void A_replayed_token_is_reuse_even_though_rotation_already_revoked_it()
    {
        // The ordering trap: rotation sets RevokedAt and ReplacedByToken together, so a stolen
        // token is always inactive by the time it comes back. Testing IsActive first would classify
        // every theft as an ordinary expiry and the reuse response would never fire.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: true, isActive: false);

        Assert.Equal(RefreshTokenVerdict.ReuseDetected, verdict);
    }

    [Fact]
    public void An_expired_or_revoked_token_is_invalid()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: false);

        Assert.Equal(RefreshTokenVerdict.Invalid, verdict);
    }

    // ── The address is no longer part of the verdict ─────────────────────────────
    // It used to be: a mismatch returned Invalid, which is a 401, which the MAUI client answers by
    // clearing its credentials. That fired on every wifi-to-cellular handover. Rotation and reuse
    // detection catch a stolen token regardless of address, so these assert the *new* behaviour
    // rather than being deleted — a silent removal is how a control comes back by accident.

    [Fact]
    public void A_token_presented_from_a_different_address_still_rotates()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: true);

        Assert.Equal(RefreshTokenVerdict.Rotate, verdict);
        Assert.True(RefreshTokenEvaluator.IsAddressChange(IssuedIp, OtherIp));
    }

    [Fact]
    public void Theft_detection_is_unaffected_by_the_address()
    {
        // The regression that matters: whatever else changed, a replayed token must still revoke the
        // family rather than being quietly rejected, so the theft is recorded.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: true, isActive: false);

        Assert.Equal(RefreshTokenVerdict.ReuseDetected, verdict);
    }

    [Theory]
    // Same address, and the two "nothing to compare" cases: a token issued before addresses were
    // captured, and a caller whose address could not be read.
    [InlineData(IssuedIp, IssuedIp)]
    [InlineData(null, IssuedIp)]
    [InlineData(null, null)]
    public void IsAddressChange_is_false_when_there_is_no_change_to_report(string? stored, string? presented)
    {
        Assert.False(RefreshTokenEvaluator.IsAddressChange(stored, presented));
    }

    [Theory]
    [InlineData(IssuedIp, OtherIp)]
    // A caller presenting no address at all, against a token that has one, is still a change worth
    // recording — it is what someone suppressing the header would look like.
    [InlineData(IssuedIp, null)]
    public void IsAddressChange_is_true_when_the_address_moved(string? stored, string? presented)
    {
        Assert.True(RefreshTokenEvaluator.IsAddressChange(stored, presented));
    }
}
