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
    public DateTime? LastUpdated { get; set; }
}

/// <summary>Service alert from GTFS-RT, with optional affected entity references.</summary>
public class AlertResponse
{
    public int Id { get; set; }
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
}
