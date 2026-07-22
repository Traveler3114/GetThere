using Microsoft.Extensions.Logging;

namespace GetThere.Services;

public class AnalyticsService : IAnalyticsService
{
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(ILogger<AnalyticsService> logger)
    {
        _logger = logger;
    }

    public void TrackEvent(string eventName, Dictionary<string, string>? properties = null)
    {
        var props = properties is not null
            ? string.Join(", ", properties.Select(kv => $"{kv.Key}={kv.Value}"))
            : "";
        _logger.LogDebug("[Analytics] Event: {EventName} [{Props}]", eventName, props);
    }

    public void TrackScreen(string screenName)
    {
        _logger.LogDebug("[Analytics] Screen: {ScreenName}", screenName);
    }

    public void IdentifyUser(string userId)
    {
        _logger.LogDebug("[Analytics] Identify: {UserId}", userId);
    }

    public void ClearIdentity()
    {
        _logger.LogDebug("[Analytics] Clear identity");
    }
}
