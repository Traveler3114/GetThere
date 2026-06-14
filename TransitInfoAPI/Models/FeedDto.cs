namespace TransitInfoAPI.Models;

public class FeedDto
{
    public int Id { get; set; }
    public string FeedType { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string? ExternalUrl { get; set; }
    public string? InternalUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastFetched { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public string? OperatorName { get; set; }
}
