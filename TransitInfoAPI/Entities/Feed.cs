using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class Feed
{
    public int Id { get; set; }
    public FeedType FeedType { get; set; }
    public SourceType SourceType { get; set; }
    public string? ExternalUrl { get; set; }
    public string? InternalUrl { get; set; }
    public string FeedId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastFetched { get; set; }
    public DateTime? LastSuccessful { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;

    public ICollection<FeedConverter> Converters { get; set; } = [];
}
