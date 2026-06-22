using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Contracts;

public class StationResponse
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string StationType { get; set; } = string.Empty;
    public string? PrimaryRouteType { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
}

public class StationOperatorResponse
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class DepartureResponse
{
    public string TripId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string Headsign { get; set; } = string.Empty;
    public DateTime? ScheduledDeparture { get; set; }
    public DateTime? EstimatedDeparture { get; set; }
    public int? DelaySeconds { get; set; }
}

public class StationDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public List<OperatorBriefResponse> Operators { get; set; } = [];
    public List<RouteInfoResponse> Routes { get; set; } = [];
}

public class StationReconciliationDetailResponse
{
    public int StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string StationGlobalId { get; set; } = string.Empty;
    public string StationOnestopId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string PrimaryRouteType { get; set; } = string.Empty;
    public List<ReconciliationEntryResponse> Entries { get; set; } = [];
}

public class ReconciliationEntryResponse
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

public class StationMergeLogResponse
{
    public int Id { get; set; }
    public int SourceStationId { get; set; }
    public string SourceStationName { get; set; } = string.Empty;
    public string SourceStationGlobalId { get; set; } = string.Empty;
    public int TargetStationId { get; set; }
    public string TargetStationName { get; set; } = string.Empty;
    public int RawStopsMovedCount { get; set; }
    public DateTime MergedAt { get; set; }
    public bool Unmerged { get; set; }
}

public class StationSplitLogResponse
{
    public int Id { get; set; }
    public int RawStopId { get; set; }
    public int FeedVersionId { get; set; }
    public int CandidateStationId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}
