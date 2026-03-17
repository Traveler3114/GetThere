namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Internal vehicle model returned by realtime parsers.
/// Never sent to the app — RealtimeManager converts this to VehicleDto
/// after enriching with route names and types from StaticDataManager.
///
/// Kept separate from the app-facing VehicleDto because parsers need
/// fields the app should never see (StopTimeUpdates, IsScheduledOnly).
/// </summary>
public class ParsedVehicle
{
    public string  VehicleId      { get; set; } = "";
    public string? TripId         { get; set; }
    public string? RouteId        { get; set; }
    public double  Lat            { get; set; }
    public double  Lon            { get; set; }
    public float   Bearing        { get; set; }
    public string? Label          { get; set; }

    /// <summary>
    /// True = TripUpdate exists but no GPS position yet.
    /// Vehicle hasn't started its trip or isn't transmitting location.
    /// Excluded from the map but delay predictions are still used.
    /// </summary>
    public bool IsScheduledOnly { get; set; }

    /// <summary>
    /// Per-stop delay predictions from the TripUpdate feed.
    /// Used by OperatorManager to annotate schedules with realtime delays.
    /// </summary>
    public List<ParsedStopTimeUpdate>? StopTimeUpdates { get; set; }
}

public class ParsedStopTimeUpdate
{
    public string? StopId       { get; set; }
    public int     StopSequence { get; set; }
    /// <summary>Delay in seconds. Negative = early, positive = late.</summary>
    public int     DelaySeconds { get; set; }
    /// <summary>Absolute Unix arrival timestamp (optional).</summary>
    public long    ArrivalUnix  { get; set; }
}
