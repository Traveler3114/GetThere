namespace TransitInfoAPI.Contracts;

/// <summary>Real-time vehicle position from GTFS-RT.</summary>
public class VehicleResponse
{
    public string VehicleId { get; set; } = string.Empty;
    public string? FeedId { get; set; }
    public string? RouteId { get; set; }
    public string? TripId { get; set; }
    public string? RouteShortName { get; set; }
    public bool IsRealtime { get; set; }
    public string? BlockId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Bearing { get; set; }
    public double? Speed { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string? OccupancyStatus { get; set; }
    public int? OccupancyPercentage { get; set; }
    public string? CongestionLevel { get; set; }
    public string? WheelchairAccessible { get; set; }
}

/// <summary>A trip update with per-stop real-time data.</summary>
public class TripUpdateResponse
{
    public string TripId { get; set; } = string.Empty;
    public string? RouteId { get; set; }
    public int? DirectionId { get; set; }
    public string? StartTime { get; set; }
    public List<StopTimeUpdateResponse> StopTimeUpdates { get; set; } = [];
}

public class StopTimeUpdateResponse
{
    public string? StopId { get; set; }
    public int? StopSequence { get; set; }
    public int DelaySeconds { get; set; }
    public long? EstimatedTime { get; set; }
}

/// <summary>Service alert from GTFS-RT or a scraped / HAK source.</summary>
public class AlertResponse
{
    public int Id { get; set; }
    public int? FeedId { get; set; }
    public int? OperatorId { get; set; }
    public string? HeaderText { get; set; }
    public string? DescriptionText { get; set; }
    public string? Url { get; set; }
    public string? Cause { get; set; }
    public string? Effect { get; set; }
    public DateTime? ActivePeriodStart { get; set; }
    public DateTime? ActivePeriodEnd { get; set; }
    public DateTime FetchedAt { get; set; }
    public string? AffectedStopIds { get; set; }
    public string? AffectedRouteIds { get; set; }
    public string? AffectedTripIds { get; set; }
    public string? AffectedAgencyIds { get; set; }

    public string? Kind { get; set; }
    public string? SourceKey { get; set; }
    public string? SourceUrl { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? GeometryGeoJson { get; set; }
    public string? Severity { get; set; }
    public string? MatchedRouteIds { get; set; }
}
