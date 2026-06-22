namespace TransitInfoAPI.Contracts;

public class FeedResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string FeedType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string? ExternalUrl { get; set; }
    public string? InternalUrl { get; set; }
    public bool IsActive { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public string? OperatorName { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
    public bool? LicenseCommercialUseAllowed { get; set; }
    public bool? LicenseShareAlikeOptional { get; set; }
    public bool? LicenseRedistributionAllowed { get; set; }
}

public class FeedVersionResponse
{
    public int Id { get; set; }
    public int FeedId { get; set; }
    public string Sha1 { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public bool IsActive { get; set; }
    public string ImportStatus { get; set; } = string.Empty;
    public string? ImportError { get; set; }
    public DateOnly? ServiceLevelStart { get; set; }
    public DateOnly? ServiceLevelEnd { get; set; }
    public int StopCount { get; set; }
    public int RouteCount { get; set; }
    public int TripCount { get; set; }
}

public class CreateFeedRequest
{
    public int OperatorId { get; set; }
    public string FeedType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string? ExternalUrl { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 3600;
}

public class UpdateFeedRequest
{
    public string FeedType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
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
