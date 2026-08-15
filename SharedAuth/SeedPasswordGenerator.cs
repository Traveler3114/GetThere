using System.Security.Cryptography;

namespace SharedAuth;

/// <summary>
/// Generates the throwaway password used when an environment seeds an account without supplying one.
/// <para>
/// Both services had their own identical copy at the bottom of <c>Program.cs</c>. It is shared for
/// the same reason the token and refresh rules are: two copies of a credential-shaping routine drift,
/// and the drift is invisible until the day one of them produces something the password policy
/// rejects.
/// </para>
/// <para>
/// This only ever runs in Development. Outside it, both services require <c>Seed:AdminPassword</c>
/// to be configured and skip seeding rather than generating anything — writing a generated
/// credential to disk on every deployment is the thing that arrangement exists to avoid.
/// </para>
/// </summary>
public static class SeedPasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*";
    private const string All = Upper + Lower + Digits + Symbols;

    /// <summary>
    /// Returns a password that satisfies the Identity policy both services configure: at least
    /// 12 characters, with an uppercase letter, a digit and a non-alphanumeric.
    /// <para>
    /// The previous implementation drew every character uniformly from the combined alphabet, which
    /// does not <em>guarantee</em> any of those classes appears. At 24 characters the odds of missing
    /// one are tiny but not zero, and the failure mode was poor: <c>CreateAsync</c> rejects the
    /// password, the account is never created, and the environment comes up with no admin at all.
    /// Seeding one character from each required class removes the possibility rather than making it
    /// unlikely.
    /// </para>
    /// </summary>
    /// <param name="length">Total length; values below 12 are raised to it to satisfy the policy.</param>
    public static string Generate(int length)
    {
        if (length < 12) length = 12;

        var chars = new char[length];

        // One guaranteed character per required class, then fill the rest freely.
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < length; i++)
            chars[i] = All[RandomNumberGenerator.GetInt32(All.Length)];

        // Shuffled, so the guaranteed characters are not always in the first four positions — a
        // predictable layout narrows the search space for anyone who knows how these are made.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
