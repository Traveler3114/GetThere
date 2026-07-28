using Microsoft.AspNetCore.Identity;

namespace GetThereAuth;

/// <summary>
/// Keeps "no such account" from being measurably faster than "wrong password".
/// <para>
/// Verifying a password costs a deliberately slow hash. Returning early when the address is unknown
/// skips that cost, and the difference is large enough to measure over the network — which turns
/// the login endpoint into an oracle for whether an address has an account here. Hashing a throwaway
/// password on the miss path spends the same time before failing.
/// </para>
/// <para>
/// GetThereAPI had this on both login and registration; TransitInfoAPI had neither, so the same
/// endpoint pair there answered the question freely. Shared so the next fix lands in both.
/// </para>
/// </summary>
public static class AccountEnumerationGuard
{
    /// <summary>
    /// Burns the same work a real password check would, so an unknown account does not answer
    /// faster than a known one. The result is deliberately discarded.
    /// </summary>
    public static void SpendPasswordHashingTime<TUser>(UserManager<TUser> userManager, string password)
        where TUser : class, new()
    {
        _ = userManager.PasswordHasher.HashPassword(new TUser(), password);
    }
}
