namespace TransitInfoAPI.Services.Otp;

internal class OtpBuildConfig
{
    public List<OtpTransitFeedConfig> transitFeeds { get; set; } = [];
    public string transitModelTimeZone { get; set; } = "Europe/Zagreb";
}

internal class OtpTransitFeedConfig
{
    public string type { get; set; } = "gtfs";
    public string source { get; set; } = string.Empty;
    public string feedId { get; set; } = string.Empty;
}

internal class OtpRouterConfig
{
    public List<OtpRouterUpdaterConfig> updaters { get; set; } = [];
}

internal class OtpRouterUpdaterConfig
{
    public string type { get; set; } = "STOP_TIME_UPDATER";
    public string feedId { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
    public string frequency { get; set; } = "PT30S";
}
