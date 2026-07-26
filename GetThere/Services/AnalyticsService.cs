using System.Diagnostics;

namespace GetThere.Services;

public class AnalyticsService : IAnalyticsService
{
    public void TrackEvent(string eventName, Dictionary<string, string>? properties = null)
    {
        var props = properties is not null ? string.Join(", ", properties) : "";
        Trace.WriteLine($"[Analytics] Event: {eventName} ({props})");
    }

    public void TrackScreen(string screenName)
    {
        Trace.WriteLine($"[Analytics] Screen: {screenName}");
    }
}
