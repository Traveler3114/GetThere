namespace TransitInfoAPI.Entities;

public class Trip
{
    public int Id { get; set; }
    public int FeedVersionId { get; set; }
    public FeedVersion FeedVersion { get; set; } = null!;

    public string TripId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string? TripHeadsign { get; set; }
    public string? TripShortName { get; set; }
    public int? DirectionId { get; set; }
    public string? ShapeId { get; set; }
    public bool? WheelchairAccessible { get; set; }
    public bool? BikesAllowed { get; set; }

    public int? CanonicalRouteId { get; set; }
    public CanonicalRoute? CanonicalRoute { get; set; }

    public ICollection<StopTime> StopTimes { get; set; } = [];
}
