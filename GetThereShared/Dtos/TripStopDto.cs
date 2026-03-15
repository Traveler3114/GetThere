namespace GetThereShared.Dtos;

/// <summary>One stop in a trip's ordered sequence, with realtime annotation.</summary>
public class TripStopDto
{
    public int     Sequence      { get; set; }
    public string  StopId        { get; set; } = "";
    public string  StopName      { get; set; } = "";
    public double  Lat           { get; set; }
    public double  Lon           { get; set; }
    public string  ScheduledTime { get; set; } = "";   // "HH:MM"
    public string? EstimatedTime { get; set; }          // null = no realtime data
    public int?    DelayMinutes  { get; set; }
    /// <summary>True if this stop is already in the past (vehicle passed it).</summary>
    public bool    IsPassed      { get; set; }
}

public class TripDetailDto
{
    public string         TripId    { get; set; } = "";
    public string         RouteId   { get; set; } = "";
    public string         ShortName { get; set; } = "";
    public string         Headsign  { get; set; } = "";
    public int            RouteType { get; set; } = 3;
    public List<TripStopDto> Stops  { get; set; } = [];
    /// <summary>Index into Stops of the current/next stop (vehicle is heading here).</summary>
    public int            CurrentStopIndex { get; set; }
    /// <summary>GPS position of the vehicle right now (for map fly-to).</summary>
    public double         VehicleLat { get; set; }
    public double         VehicleLon { get; set; }
    public bool           IsTracked  { get; set; }
}
