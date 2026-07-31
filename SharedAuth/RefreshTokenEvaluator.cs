namespace SharedAuth;

/// <summary>What a presented refresh token entitles the caller to.</summary>
public enum RefreshTokenVerdict
{
    /// <summary>Valid: revoke it, issue a replacement.</summary>
    Rotate,

    /// <summary>Already rotated once and presented again — treat as theft and revoke the family.</summary>
    ReuseDetected,

    /// <summary>Unknown, expired, or revoked.</summary>
    Invalid
}

/// <summary>
/// Decides what to do with a presented refresh token.
/// <para>
/// The rules are short but each one is load-bearing, and they lived as two hand-maintained copies —
/// one per API — which is exactly the kind of duplication that goes wrong quietly. Keeping the
/// decision here (and the database writes in each API's own manager) means the two cannot disagree
/// about what counts as theft.
/// </para>
/// <para>
/// <b>Rotation plus reuse detection is the whole of the theft response.</b> A stolen token replayed
/// after the legitimate client has refreshed hits <see cref="RefreshTokenVerdict.ReuseDetected"/> and
/// the user's entire token family is revoked. That holds no matter where either party is connecting
/// from, which is why the address check that used to sit here could be removed without weakening it —
/// see <see cref="IsAddressChange"/>.
/// </para>
/// </summary>
public static class RefreshTokenEvaluator
{
    /// <param name="found">Whether a stored token matched the presented hash.</param>
    /// <param name="hasReplacement">Whether the stored token already names a successor.</param>
    /// <param name="isActive">Whether the stored token is neither expired nor revoked.</param>
    public static RefreshTokenVerdict Evaluate(
        bool found,
        bool hasReplacement,
        bool isActive)
    {
        if (!found)
            return RefreshTokenVerdict.Invalid;

        // Must be tested before the IsActive guard below. Rotation sets RevokedAt *and*
        // ReplacedByToken together, so a replayed token is already inactive: checking IsActive first
        // would send every stolen token down the ordinary "expired" path and the theft would never
        // be detected.
        if (hasReplacement)
            return RefreshTokenVerdict.ReuseDetected;

        if (!isActive)
            return RefreshTokenVerdict.Invalid;

        return RefreshTokenVerdict.Rotate;
    }

    /// <summary>
    /// Whether a token is being presented from a different address than it was issued to.
    /// <para>
    /// <b>Telemetry only — this must not decide a verdict.</b> It used to: an address change returned
    /// <see cref="RefreshTokenVerdict.Invalid"/>, which becomes a 401, which the MAUI client answers
    /// by clearing its credentials and returning to the login screen. On a phone that fired on every
    /// wifi-to-cellular handover, every cell handover, every CGNAT rebinding and every IPv6
    /// privacy-extension rotation — so a travel app whose users are by definition moving signed them
    /// out repeatedly, and took any offline-cached ticket with it.
    /// </para>
    /// <para>
    /// It was not buying much in exchange. Rotation and reuse detection already catch a stolen token
    /// regardless of address. And both APIs run <c>UseForwardedHeaders</c> with the known-proxy lists
    /// cleared, so <c>X-Forwarded-For</c> is honoured from any immediate peer: an attacker holding a
    /// stolen token could simply assert the address the check wanted. It punished honest users
    /// reliably and a capable attacker not at all.
    /// </para>
    /// <para>
    /// The signal is still worth recording, so callers audit it. A real binding would be to the
    /// device rather than the network — a client-generated identifier that survives a change of
    /// network but not a change of hardware. Note <c>RefreshToken.DeviceInfo</c> is <b>not</b> that
    /// today: it is the raw <c>User-Agent</c>, caller-supplied and not unique.
    /// </para>
    /// </summary>
    public static bool IsAddressChange(string? storedAddress, string? presentedAddress) =>
        storedAddress is not null && storedAddress != presentedAddress;
}
