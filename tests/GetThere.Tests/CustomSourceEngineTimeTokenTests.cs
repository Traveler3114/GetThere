using TransitInfoAPI.Services;

namespace GetThere.Tests;

public class CustomSourceEngineTimeTokenTests
{
    [Fact]
    public void ApplyTimeTokens_resolves_all_known_tokens()
    {
        var fixedNow = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var cases = new Dictionary<string, string>
        {
            ["https://x.test/a?from={now}"] = "2026-08-22T12:00:00.000Z",
            ["https://x.test/a?from={now+90m}"] = "2026-08-22T13:30:00.000Z",
            ["https://x.test/a?from={now-15m}"] = "2026-08-22T11:45:00.000Z",
            ["https://x.test/a?from={now+2h}"] = "2026-08-22T14:00:00.000Z",
            ["https://x.test/a?date={today}"] = "2026-08-22",
        };
        foreach (var (input, expected) in cases)
        {
            var result = CustomSourceEngine.ApplyTimeTokens(input, fixedNow);
            Assert.Contains(expected, result);
        }
    }

    [Fact]
    public void ApplyTimeTokens_leaves_unknown_token_untouched()
    {
        var fixedNow = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var input = "https://x.test/a?foo={bogus}";
        var result = CustomSourceEngine.ApplyTimeTokens(input, fixedNow);
        Assert.Equal(input, result);
    }
}
