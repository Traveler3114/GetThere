namespace TransitInfoAPI.Workers;

/// <summary>
/// Turns a configured polling interval into one that is safe to hand to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// <para>
/// Two of the three workers here read <c>IOptionsMonitor.CurrentValue</c> and passed the number
/// straight through. Zero meant a tight loop against the database and every operator's endpoint;
/// a <b>negative</b> number meant <c>Task.Delay</c> threw <see cref="ArgumentOutOfRangeException"/>,
/// and because those delays sit outside the try/catch that wraps the poll body, the exception escaped
/// <c>ExecuteAsync</c> — where the default <c>BackgroundServiceExceptionBehavior.StopHost</c> takes
/// the whole service down. One mistyped number in <c>appsettings.json</c> was enough, and because
/// <c>CurrentValue</c> is re-read every cycle, a bad hot-reload could stop a service that was already
/// running healthily.
/// </para>
/// <para>
/// GetThereAPI's workers already clamp in their constructors — see
/// <c>TicketExpiryWorker.MinimumInterval</c>, whose comment gives the reason: "a configured 0 would
/// otherwise spin against the database". This is that idea, shared, because there are three workers
/// here and <c>FeedPollingWorker</c> had already grown its own inline copy of half of it.
/// </para>
/// </summary>
internal static class PollingInterval
{
    /// <summary>
    /// Floor on a repeating interval. Deliberately low — the point is to stop a spin loop and a
    /// crash, not to overrule a deployment that genuinely wants fast polling.
    /// </summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A repeating interval, in seconds. Zero and negative fall back to <paramref name="fallbackSeconds"/>;
    /// anything below <see cref="Minimum"/> is raised to it.
    /// </summary>
    public static TimeSpan Seconds(int configured, int fallbackSeconds, ILogger logger, string setting) =>
        Resolve(TimeSpan.FromSeconds(configured), TimeSpan.FromSeconds(fallbackSeconds), logger, setting);

    /// <summary>A repeating interval, in minutes. Same rules as <see cref="Seconds"/>.</summary>
    public static TimeSpan Minutes(int configured, int fallbackMinutes, ILogger logger, string setting) =>
        Resolve(TimeSpan.FromMinutes(configured), TimeSpan.FromMinutes(fallbackMinutes), logger, setting);

    /// <summary>
    /// A one-off startup delay. Unlike an interval, zero is a legitimate value here — "start
    /// immediately" — so only a negative is corrected.
    /// </summary>
    public static TimeSpan InitialDelaySeconds(int configured, int fallbackSeconds, ILogger logger, string setting)
    {
        if (configured >= 0) return TimeSpan.FromSeconds(configured);

        logger.LogWarning(
            "{Setting} is configured as {Configured}, which is not a valid delay. Using {Fallback}s instead.",
            setting, configured, fallbackSeconds);
        return TimeSpan.FromSeconds(fallbackSeconds);
    }

    private static TimeSpan Resolve(TimeSpan configured, TimeSpan fallback, ILogger logger, string setting)
    {
        if (configured <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "{Setting} is configured as {Configured}, which would poll without pausing. Using {Fallback} instead.",
                setting, configured, fallback);
            return fallback;
        }

        if (configured < Minimum)
        {
            logger.LogWarning(
                "{Setting} is configured as {Configured}, below the {Minimum} floor. Using the floor instead.",
                setting, configured, Minimum);
            return Minimum;
        }

        return configured;
    }
}
