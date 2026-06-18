using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Models;

public class UpdateFeedRequest
{
    public FeedType FeedType { get; set; }
    public string? ExternalUrl { get; set; }
    public string? InternalUrl { get; set; }
    public bool IsActive { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
    public bool? LicenseCommercialUseAllowed { get; set; }
    public bool? LicenseShareAlikeOptional { get; set; }
    public bool? LicenseRedistributionAllowed { get; set; }
}
