namespace GetThereShared.Dtos;

public class OtpOperatorFeedDto
{
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public string OtpFeedId { get; set; } = string.Empty;
    public string OtpInstanceKey { get; set; } = string.Empty;
    public string? StaticGtfsUrl { get; set; }
    public string? LegacyGtfsRealtimeUrl { get; set; }
    public string? TripUpdatesUrl { get; set; }
    public string? VehiclePositionsUrl { get; set; }
    public string? AlertsUrl { get; set; }
    public string RealtimeFallbackMode { get; set; } = "None";
}
