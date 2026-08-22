namespace GetThere.Tests;

public class AlertPollingCadenceTests
{
    private static bool IsDue(DateTime? lastRunAt, int intervalMinutes, DateTime now)
    {
        // Mirrors AlertPollingWorker predicate with 30s slack
        return lastRunAt is null || lastRunAt <= now.AddMinutes(-intervalMinutes).AddSeconds(30);
    }

    [Fact]
    public void Source_due_at_exactly_15_minutes_plus_two_seconds_is_selected()
    {
        var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var lastRun = now.AddMinutes(-15).AddSeconds(-2);
        Assert.True(IsDue(lastRun, 15, now));
    }

    [Fact]
    public void Source_not_due_is_not_selected()
    {
        var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var lastRun = now.AddMinutes(-14);
        Assert.False(IsDue(lastRun, 15, now));
    }

    [Fact]
    public void Never_run_source_is_always_due()
    {
        var now = DateTime.UtcNow;
        Assert.True(IsDue(null, 15, now));
    }

    [Fact]
    public void Without_slack_source_due_would_be_skipped_due_to_fetch_duration()
    {
        // Demonstrates the bug: without +30s, a source that ran 15m and 5 seconds ago, where fetch took 5 seconds, appears not due
        var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var lastRun = now.AddMinutes(-15).AddSeconds(2); // stamped after fetch, so 2 seconds after interval
        // Old predicate: lastRun <= now -15m ?
        var old = lastRun <= now.AddMinutes(-15);
        // New predicate with slack:
        var @new = IsDue(lastRun, 15, now);
        Assert.False(old);
        Assert.True(@new);
    }
}
