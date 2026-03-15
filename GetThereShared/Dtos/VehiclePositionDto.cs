namespace GetThereShared.Dtos;

public class VehiclePositionDto
{
    public string VehicleId { get; set; } = "";
    public string? RouteId { get; set; }
    public string? TripId { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public float Bearing { get; set; }
    public string? Label { get; set; }
    /// <summary>True for TripUpdate-only entries with no GPS position.</summary>
    public bool IsScheduledOnly { get; set; }
    /// <summary>Per-stop arrival predictions from TripUpdate feed.</summary>
    public List<StopTimeUpdateDto>? StopTimeUpdates { get; set; }
}

public class StopTimeUpdateDto
{
    public string? StopId { get; set; }
    public int StopSequence { get; set; }
    /// <summary>Arrival delay in seconds. Negative = early, positive = late.</summary>
    public int DelaySeconds { get; set; }
    /// <summary>Absolute arrival Unix timestamp (optional, some stops only have delay).</summary>
    public long ArrivalUnix { get; set; }
}