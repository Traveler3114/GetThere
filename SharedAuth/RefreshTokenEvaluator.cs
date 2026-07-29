namespace SharedAuth;

/// <summary>What a presented refresh token entitles the caller to.</summary>
public enum RefreshTokenVerdict
{
    /// <summary>Valid: revoke it, issue a replacement.</summary>
    Rotate,

    /// <summary>Already rotated once and presented again — treat as theft and revoke the family.</summary>
    ReuseDetected,

    /// <summary>Unknown, expired, revoked, or presented from the wrong address.</summary>
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
/// </summary>
public static class RefreshTokenEvaluator
{
    /// <param name="found">Whether a stored token matched the presented hash.</param>
    /// <param name="hasReplacement">Whether the stored token already names a successor.</param>
    /// <param name="isActive">Whether the stored token is neither expired nor revoked.</param>
    /// <param name="storedIpAddress">Address the token was issued to, if one was captured.</param>
    /// <param name="presentedIpAddress">Address presenting it now.</param>
    public static RefreshTokenVerdict Evaluate(
        bool found,
        bool hasReplacement,
        bool isActive,
        string? storedIpAddress,
        string? presentedIpAddress)
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

        // A caller presenting no address at all is rejected when the token was issued with one,
        // otherwise suppressing the address is enough to skip the check entirely. A token stored
        // without an address (issued before addresses were captured) cannot be compared, so it is
        // allowed through.
        if (storedIpAddress is not null && storedIpAddress != presentedIpAddress)
            return RefreshTokenVerdict.Invalid;

        return RefreshTokenVerdict.Rotate;
    }
}
