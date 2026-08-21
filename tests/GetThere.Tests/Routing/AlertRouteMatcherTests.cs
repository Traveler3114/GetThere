using TransitInfoAPI.Services;

namespace GetThere.Tests.Routing;

/// <summary>
/// The line-number regex is what decides which routes an operator's free-text notice is about, and a
/// wrong match becomes a real disruption on a real route once it reaches OTP. Every phrasing here was
/// taken from a live notice page (2026-08-21), so these pin the shapes actually published.
/// </summary>
public class AlertRouteMatcherTests
{
    [Theory]
    // Autotrolej
    [InlineData("Privremena izmjena trase linije 12A", new[] { "12A" })]
    // Promet Split
    [InlineData("OBAVIJEST ZA PUTNIKE NA LINIJI BR. 33 SPLIT - KOSA - SPLIT", new[] { "33" })]
    [InlineData("Linija broj 1 vraćena na uobičajenu trasu", new[] { "1" })]
    // GPP Osijek
    [InlineData("Tramvajska linija T1 vozi obilazno zbog radova", new[] { "T1" })]
    public void ExtractsLineNumbersFromLiveNoticeTitles(string title, string[] expected)
    {
        Assert.Equal(expected, AlertRouteMatcher.ExtractTokens(title));
    }

    [Fact]
    public void ExtractsEveryLineFromAListedNotice()
    {
        // ZET publishes several lines in one notice; missing the tail would leave routes unflagged.
        var tokens = AlertRouteMatcher.ExtractTokens("Linije 6, 8 i 14 mijenjaju trase prometovanja");

        Assert.Equal(new[] { "6", "8", "14" }, tokens);
    }

    [Theory]
    [InlineData("Radovi na pruzi Zagreb GK - Dugo Selo")]
    [InlineData("Obavijest korisnicima o novom cjeniku")]
    [InlineData("")]
    public void YieldsNothingWhenNoLineIsNamed(string text)
    {
        // A notice with no line must match nothing rather than fall back to a guess — an unmatched
        // alert is still shown, it simply does not claim to affect a specific route.
        Assert.Empty(AlertRouteMatcher.ExtractTokens(text));
    }
}
