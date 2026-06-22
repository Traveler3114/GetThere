namespace TransitInfoAPI.Contracts;

public class ReconciliationResponse
{
    public int Id { get; set; }
    public int RawStopId { get; set; }
    public string RawStopName { get; set; } = string.Empty;
    public double RawStopLat { get; set; }
    public double RawStopLon { get; set; }
    public string? RawStopGtfsId { get; set; }
    public string? RawRouteType { get; set; }
    public string? CanonicalRouteType { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal NameSimilarityScore { get; set; }
    public decimal DistanceMeters { get; set; }
    public bool NameMatched { get; set; }
    public bool DistanceMatched { get; set; }
    public bool RouteTypeMatched { get; set; }
    public bool AutoReconciled { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? FeedId { get; set; }
    public int? SuggestedStationId { get; set; }
    public string? SuggestedStationName { get; set; }
    public double? SuggestedStationLat { get; set; }
    public double? SuggestedStationLon { get; set; }
    public string? NormalizedRawName { get; set; }
    public string? NormalizedStationName { get; set; }
    public string? MatchExplanation { get; set; }
    public double? AutoMergeNameThreshold { get; set; }
    public double? AutoMergeDistanceMeters { get; set; }
    public double? ManualReviewNameThreshold { get; set; }
    public double? ManualReviewDistanceMeters { get; set; }
}

public class ReconciliationDetailResponse : ReconciliationResponse
{
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByAdminId { get; set; }
    public string? RawStopCountry { get; set; }
    public StationDetailResponse? RawStopDetail { get; set; }
    public StationDetailResponse? SuggestedStationDetail { get; set; }
    public string AutoMergeVerdict { get; set; } = string.Empty;
}
