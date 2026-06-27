using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class Feed
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public FeedType FeedType { get; set; }
    public string? Url { get; set; }
    public string FeedId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsInternal { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
    public bool? LicenseCommercialUseAllowed { get; set; }
    public bool? LicenseShareAlikeOptional { get; set; }
    public bool? LicenseRedistributionAllowed { get; set; }

    public string? SupersedesIds { get; set; }

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;

    public int? CustomFeedId { get; set; }
    public CustomFeed? CustomFeed { get; set; }

    public ICollection<FeedVersion> FeedVersions { get; set; } = [];
}
