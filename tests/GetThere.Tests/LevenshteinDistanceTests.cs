using TransitInfoAPI.Managers;

namespace GetThere.Tests;

/// <summary>
/// The distance function was rewritten from a full m×n matrix to two rolling rows (it runs once per
/// candidate pair across a feed import). These pin the behaviour so the optimisation cannot change
/// station matching results.
/// </summary>
public class LevenshteinDistanceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("a", "", 1)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("saturday", "sunday", 3)]
    [InlineData("Zagreb Glavni Kolodvor", "Zagreb Glavni kolodvor", 1)]
    [InlineData("Ljubljana", "Ljubjlana", 2)]
    public void Computes_known_distances(string a, string b, int expected)
    {
        Assert.Equal(expected, ReconciliationManager.LevenshteinDistance(a, b));
    }

    [Theory]
    [InlineData("kitten", "sitting")]
    [InlineData("a", "abcdefghij")]
    [InlineData("Zagreb", "Zagreb Glavni Kolodvor")]
    public void Is_symmetric(string a, string b)
    {
        Assert.Equal(
            ReconciliationManager.LevenshteinDistance(a, b),
            ReconciliationManager.LevenshteinDistance(b, a));
    }

    [Fact]
    public void Handles_strings_longer_than_the_stack_allocation_threshold()
    {
        // The implementation switches from stackalloc to a heap array at 256 chars; exercise both
        // sides of that boundary and the crossover itself.
        var baseline = new string('x', 300);
        var mutated = baseline[..299] + "y";

        Assert.Equal(1, ReconciliationManager.LevenshteinDistance(baseline, mutated));
        Assert.Equal(0, ReconciliationManager.LevenshteinDistance(baseline, baseline));
        Assert.Equal(300, ReconciliationManager.LevenshteinDistance(baseline, string.Empty));
    }

    [Fact]
    public void Handles_the_stack_allocation_boundary_exactly()
    {
        var justUnder = new string('a', 255);
        var justOver = new string('a', 256);

        Assert.Equal(1, ReconciliationManager.LevenshteinDistance(justUnder, justOver));
    }

    [Fact]
    public void Matches_a_reference_implementation_on_random_input()
    {
        var random = new Random(20260727);
        const string alphabet = "abcdeFG ";

        for (var iteration = 0; iteration < 300; iteration++)
        {
            var a = RandomString(random, alphabet, random.Next(0, 40));
            var b = RandomString(random, alphabet, random.Next(0, 40));

            Assert.Equal(ReferenceDistance(a, b), ReconciliationManager.LevenshteinDistance(a, b));
        }
    }

    private static string RandomString(Random random, string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
        return new string(chars);
    }

    /// <summary>The original full-matrix implementation, kept here as the oracle.</summary>
    private static int ReferenceDistance(string s, string t)
    {
        var m = s.Length;
        var n = t.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) d[i, 0] = i;
        for (var j = 0; j <= n; j++) d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}
