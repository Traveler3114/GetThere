namespace GetThereShared.Dtos;

public class OtpOperatorFeedDto
{
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string? StaticGtfsUrl { get; set; }
    public string? GtfsRealtimeUrl { get; set; }
}