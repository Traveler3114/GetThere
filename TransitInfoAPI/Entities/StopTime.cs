namespace TransitInfoAPI.Entities;

public class StopTime
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public string RawStopId { get; set; } = string.Empty;
    public int? CanonicalStationId { get; set; }
    public CanonicalStation? CanonicalStation { get; set; }

    public TimeSpan ArrivalTime { get; set; }
    public TimeSpan DepartureTime { get; set; }
    public int StopSequence { get; set; }
    public string? StopHeadsign { get; set; }
    public int? PickupType { get; set; }
    public int? DropOffType { get; set; }
    public bool? Timepoint { get; set; }
}
