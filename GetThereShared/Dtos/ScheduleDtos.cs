namespace GetThereShared.Dtos;

/// <summary>
/// Full schedule for one stop, grouped by route + direction.
/// Returned by GET /stops/{stopId}/schedule
/// </summary>
public class StopScheduleDto
{
    public string                  StopId   { get; set; } = "";
    public string                  StopName { get; set; } = "";
    public List<DepartureGroupDto> Groups   { get; set; } = [];
}

/// <summary>
/// All departures for one route + direction at a stop.
/// e.g. Tram 6 → Črnomerec
/// </summary>
public class DepartureGroupDto
{
    public string             RouteId    { get; set; } = "";
    public string             ShortName  { get; set; } = "";
    public string             Headsign   { get; set; } = "";
    public List<DepartureDto> Departures { get; set; } = [];
}

/// <summary>A single departure, with optional realtime delay.</summary>
public class DepartureDto
{
    public string  TripId        { get; set; } = "";
    public string  ScheduledTime { get; set; } = "";   // "HH:MM"

    /// <summary>Null if no realtime data for this departure.</summary>
    public string? EstimatedTime { get; set; }
    public int?    DelayMinutes  { get; set; }

    /// <summary>True = vehicle has a live GPS fix.</summary>
    public bool    IsRealtime    { get; set; }
}

/// <summary>
/// Full stop sequence for one trip, with realtime annotations.
/// Returned by GET /trips/{tripId}
/// </summary>
public class TripDetailDto
{
    public string            TripId           { get; set; } = "";
    public string            RouteId          { get; set; } = "";
    public string            ShortName        { get; set; } = "";
    public string            Headsign         { get; set; } = "";

    /// <summary>GTFS route_type. Drives pill colour in the UI.</summary>
    public int               RouteType        { get; set; } = 3;

    public List<TripStopDto> Stops            { get; set; } = [];

    /// <summary>Index into Stops — the stop the vehicle is currently heading to.</summary>
    public int               CurrentStopIndex { get; set; }

    /// <summary>Live GPS position of the vehicle. Zero if not tracked.</summary>
    public double            VehicleLat       { get; set; }
    public double            VehicleLon       { get; set; }

    /// <summary>True = vehicle has a live GPS fix right now.</summary>
    public bool              IsRealtime       { get; set; }

    public string?           VehicleId        { get; set; }
    public string?           BlockId          { get; set; }
}

/// <summary>One stop in a trip's ordered stop sequence.</summary>
public class TripStopDto
{
    public int     Sequence      { get; set; }
    public string  StopId        { get; set; } = "";
    public string  StopName      { get; set; } = "";
    public double  Lat           { get; set; }
    public double  Lon           { get; set; }
    public string  ScheduledTime { get; set; } = "";   // "HH:MM"

    /// <summary>Null if no realtime data for this stop.</summary>
    public string? EstimatedTime { get; set; }
    public int?    DelayMinutes  { get; set; }

    /// <summary>True if the vehicle has already passed this stop.</summary>
    public bool    IsPassed      { get; set; }
}
