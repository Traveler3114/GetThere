namespace TransitInfoAPI.Models;

public class StationReconciliationDetailDto
{
    public int StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string StationGlobalId { get; set; } = string.Empty;
    public string StationOnestopId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string PrimaryRouteType { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Entries { get; set; } = [];
}

public class ReconciliationEntryDto
{
    public int RawStopId { get; set; }
    public string RawStopName { get; set; } = string.Empty;
    public string? RawStopGtfsId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RawRouteType { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal NameSimilarityScore { get; set; }
    public decimal DistanceMeters { get; set; }
    public bool NameMatched { get; set; }
    public bool DistanceMatched { get; set; }
    public bool RouteTypeMatched { get; set; }
    public bool AutoReconciled { get; set; }
    public string? MatchExplanation { get; set; }
    public string? AutoMergeVerdict { get; set; }
    public string? OperatorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? FeedId { get; set; }

    public List<string>? MatchedLines { get; set; }
    public List<string>? UnmatchedLines { get; set; }
    public List<string>? DirectionDisagreements { get; set; }
}
