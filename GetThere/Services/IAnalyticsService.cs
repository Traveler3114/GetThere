namespace GetThere.Services;

public interface IAnalyticsService
{
    void TrackEvent(string eventName, Dictionary<string, string>? properties = null);
    void TrackScreen(string screenName);
    void IdentifyUser(string userId);
    void ClearIdentity();
}
