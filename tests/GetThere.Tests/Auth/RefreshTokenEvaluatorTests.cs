using GetThereAuth;

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

    [Fact]
    public void An_unknown_token_is_invalid()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: false, hasReplacement: false, isActive: false,
            storedIpAddress: null, presentedIpAddress: IssuedIp);

        Assert.Equal(RefreshTokenVerdict.Invalid, verdict);
    }

    [Fact]
    public void A_live_token_from_the_issuing_address_rotates()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: true,
            storedIpAddress: IssuedIp, presentedIpAddress: IssuedIp);

        Assert.Equal(RefreshTokenVerdict.Rotate, verdict);
    }

    [Fact]
    public void A_replayed_token_is_reuse_even_though_rotation_already_revoked_it()
    {
        // The ordering trap: rotation sets RevokedAt and ReplacedByToken together, so a stolen
        // token is always inactive by the time it comes back. Testing IsActive first would classify
        // every theft as an ordinary expiry and the reuse response would never fire.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: true, isActive: false,
            storedIpAddress: IssuedIp, presentedIpAddress: IssuedIp);

        Assert.Equal(RefreshTokenVerdict.ReuseDetected, verdict);
    }

    [Fact]
    public void Reuse_outranks_a_mismatched_address()
    {
        // A thief on another connection must still trigger the family revocation rather than a
        // quiet rejection, otherwise the theft goes unrecorded.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: true, isActive: false,
            storedIpAddress: IssuedIp, presentedIpAddress: "198.51.100.7");

        Assert.Equal(RefreshTokenVerdict.ReuseDetected, verdict);
    }

    [Fact]
    public void An_expired_or_revoked_token_is_invalid()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: false,
            storedIpAddress: IssuedIp, presentedIpAddress: IssuedIp);

        Assert.Equal(RefreshTokenVerdict.Invalid, verdict);
    }

    [Fact]
    public void A_token_presented_from_a_different_address_is_invalid()
    {
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: true,
            storedIpAddress: IssuedIp, presentedIpAddress: "198.51.100.7");

        Assert.Equal(RefreshTokenVerdict.Invalid, verdict);
    }

    [Fact]
    public void Suppressing_the_address_does_not_skip_the_check()
    {
        // Otherwise the binding is worth nothing: anyone holding the token just omits the header.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: true,
            storedIpAddress: IssuedIp, presentedIpAddress: null);

        Assert.Equal(RefreshTokenVerdict.Invalid, verdict);
    }

    [Fact]
    public void A_token_stored_without_an_address_is_allowed_through()
    {
        // Issued before addresses were captured. There is nothing to compare, and rejecting these
        // would log out every user holding one.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: true, hasReplacement: false, isActive: true,
            storedIpAddress: null, presentedIpAddress: IssuedIp);

        Assert.Equal(RefreshTokenVerdict.Rotate, verdict);
    }
}
