namespace OpenTripPlannerAPI.Core;

public static class RealtimeRouteConventions
{
    public const string GenericFeedRoute = "/rt/{feedId}";
    public const string LegacyFeedBySuffixRoute = "/{feedId}-rt";
    public const string LegacyHzppRoute = "/hzpp-rt";

    public static bool IsLocalRealtimePath(string absolutePath)
        => absolutePath.StartsWith("/rt/", StringComparison.OrdinalIgnoreCase)
           || absolutePath.EndsWith("-rt", StringComparison.OrdinalIgnoreCase)
           || absolutePath.Equals(LegacyHzppRoute, StringComparison.OrdinalIgnoreCase);
}
