using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Workers;

namespace GetThere.Tests;

/// <summary>
/// The three polling workers used to pass their configured interval straight to <c>Task.Delay</c>.
/// Zero spun the loop against the database and every operator's endpoint; a negative threw
/// <see cref="ArgumentOutOfRangeException"/> from a <c>Task.Delay</c> that sits outside the
/// try/catch around the poll body, so it escaped <c>ExecuteAsync</c> — where the default
/// <c>BackgroundServiceExceptionBehavior.StopHost</c> stops the whole service.
/// <para>
/// These pin the clamp rather than the workers, because that is where the decision lives now and it
/// needs no host, no database and no timing to verify.
/// </para>
/// </summary>
public class PollingIntervalTests
{
    private static readonly NullLogger Logger = NullLogger.Instance;

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Non_positive_seconds_fall_back(int configured)
    {
        var result = PollingInterval.Seconds(configured, 30, Logger, "Test:IntervalSeconds");

        Assert.Equal(TimeSpan.FromSeconds(30), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_minutes_fall_back(int configured)
    {
        var result = PollingInterval.Minutes(configured, 60, Logger, "Test:IntervalMinutes");

        Assert.Equal(TimeSpan.FromMinutes(60), result);
    }

    /// <summary>
    /// A positive value below the floor is raised rather than rejected — the deployment clearly
    /// wanted fast polling, it just asked for something that would hammer the source.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Below_the_floor_is_raised_to_it(int configured)
    {
        var result = PollingInterval.Seconds(configured, 30, Logger, "Test:IntervalSeconds");

        Assert.Equal(PollingInterval.Minimum, result);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(3600)]
    public void A_sensible_value_is_left_alone(int configured)
    {
        var result = PollingInterval.Seconds(configured, 30, Logger, "Test:IntervalSeconds");

        Assert.Equal(TimeSpan.FromSeconds(configured), result);
    }

    /// <summary>
    /// Zero is legitimate for a startup delay — "poll immediately" — so unlike an interval it is
    /// not corrected. Only a negative is, because only a negative throws.
    /// </summary>
    [Fact]
    public void A_zero_initial_delay_is_allowed()
    {
        var result = PollingInterval.InitialDelaySeconds(0, 10, Logger, "Test:InitialDelaySeconds");

        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact]
    public void A_negative_initial_delay_falls_back()
    {
        var result = PollingInterval.InitialDelaySeconds(-5, 10, Logger, "Test:InitialDelaySeconds");

        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    /// <summary>
    /// The property that actually matters: whatever comes out is something Task.Delay accepts.
    /// Task.Delay throws for any negative TimeSpan other than Timeout.InfiniteTimeSpan.
    /// </summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void The_result_is_always_a_delay_Task_Delay_accepts(int configured)
    {
        Assert.True(PollingInterval.Seconds(configured, 30, Logger, "s") > TimeSpan.Zero);
        Assert.True(PollingInterval.InitialDelaySeconds(configured, 10, Logger, "d") >= TimeSpan.Zero);
    }
}
