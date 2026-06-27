using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

/// <summary>Feed metadata including operator association and licensing.</summary>
public class FeedResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string FeedType { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string? ExternalUrl { get; set; }
    public string? InternalUrl { get; set; }
    public bool IsActive { get; set; }
    public bool IsInternal { get; set; }
    public int? CustomFeedId { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public string? OperatorName { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
    public bool? LicenseCommercialUseAllowed { get; set; }
    public bool? LicenseShareAlikeOptional { get; set; }
    public bool? LicenseRedistributionAllowed { get; set; }
}

/// <summary>A specific import version of a feed snapshot, including service-level metadata and import status.</summary>
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
    public int AgencyCount { get; set; }
}

/// <summary>Request body for creating a new feed.</summary>
public class CreateFeedRequest
{
    [Range(1, int.MaxValue)] public int OperatorId { get; set; }
    [Required, StringLength(50)] public string FeedType { get; set; } = string.Empty;
    [Required, StringLength(200)] public string FeedId { get; set; } = string.Empty;
    [Url] public string? ExternalUrl { get; set; }
    [Range(60, int.MaxValue)] public int RefreshIntervalSeconds { get; set; } = 3600;
}

/// <summary>Request body for updating an existing feed.</summary>
public class UpdateFeedRequest
{
    [Required, StringLength(50)] public string FeedType { get; set; } = string.Empty;
    [Url] public string? ExternalUrl { get; set; }
    [Url] public string? InternalUrl { get; set; }
    public bool IsActive { get; set; }
    [Range(60, int.MaxValue)] public int RefreshIntervalSeconds { get; set; }
    [StringLength(200)] public string? LicenseName { get; set; }
    [Url] public string? LicenseUrl { get; set; }
    public bool? LicenseCommercialUseAllowed { get; set; }
    public bool? LicenseShareAlikeOptional { get; set; }
    public bool? LicenseRedistributionAllowed { get; set; }
}
