using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class ReconciliationCandidate
{
    public int Id { get; set; }
    public int RawStopId { get; set; }
    public RawStop RawStop { get; set; } = null!;

    public string RawStopName { get; set; } = string.Empty;
    public double RawStopLat { get; set; }
    public double RawStopLon { get; set; }

    public RouteType RawRouteType { get; set; }
    public RouteType? CanonicalRouteType { get; set; }

    public bool NameMatched { get; set; }
    public bool DistanceMatched { get; set; }
    public bool RouteTypeMatched { get; set; }
    public bool AutoReconciled { get; set; }

    public int? SuggestedCanonicalStationId { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal DistanceMeters { get; set; }
    public decimal NameSimilarityScore { get; set; }
    public ReconciliationStatus Status { get; set; }
    public string? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

    public CanonicalStation? SuggestedCanonicalStation { get; set; }

    public decimal? AutoMergeNameThresholdAtDecision { get; set; }
    public decimal? AutoMergeDistanceMetersAtDecision { get; set; }
    public decimal? ManualReviewNameThresholdAtDecision { get; set; }
    public decimal? ManualReviewDistanceMetersAtDecision { get; set; }
}
