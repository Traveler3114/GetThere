using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Models;

public class CreateFeedRequest
{
    public int OperatorId { get; set; }
    public FeedType FeedType { get; set; }
    public SourceType SourceType { get; set; }
    public string FeedId { get; set; } = string.Empty;
    public string? ExternalUrl { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 3600;
}
