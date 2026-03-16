namespace GetThereShared.Dtos;

/// <summary>
/// A transit stop shown on the map.
/// Returned by GET /stops
/// </summary>
public class StopDto
{
    public string StopId   { get; set; } = "";
    public string Name     { get; set; } = "";
    public double Lat      { get; set; }
    public double Lon      { get; set; }

    /// <summary>
    /// GTFS route_type for the dominant service at this stop.
    /// 0=tram, 1=subway, 2=rail, 3=bus, 4=ferry
    /// Drives which icon the map renders.
    /// </summary>
    public int RouteType { get; set; } = 3;
}

/// <summary>
/// A vehicle currently on the map with a live GPS position.
/// Returned by GET /vehicles
/// Server-internal fields (StopTimeUpdates, IsScheduledOnly)
/// are never included — the server merges those before responding.
/// </summary>
public class VehicleDto
{
    public string  VehicleId      { get; set; } = "";
    public string? TripId         { get; set; }
    public string? RouteId        { get; set; }
    public string  RouteShortName { get; set; } = "";

    /// <summary>
    /// GTFS route_type. Drives icon colour and shape on the map.
    /// 0=tram, 1=subway, 2=rail, 3=bus, 4=ferry
    /// </summary>
    public int    RouteType  { get; set; } = 3;

    public double Lat        { get; set; }
    public double Lon        { get; set; }
    public float  Bearing    { get; set; }

    /// <summary>
    /// True = live GPS fix right now → green icon.
    /// False = predicted/interpolated position → grey icon.
    /// </summary>
    public bool   IsRealtime { get; set; }

    public string? BlockId   { get; set; }
}

/// <summary>
/// A transit route with its polyline shape for drawing on the map.
/// Returned by GET /routes
/// </summary>
public class RouteDto
{
    public string         RouteId   { get; set; } = "";
    public string         ShortName { get; set; } = "";
    public string         LongName  { get; set; } = "";

    /// <summary>Hex colour without #, e.g. "e53935". Null = use default for RouteType.</summary>
    public string?        Color     { get; set; }

    /// <summary>GTFS route_type. 0=tram, 1=subway, 2=rail, 3=bus, 4=ferry</summary>
    public int            RouteType { get; set; }

    /// <summary>
    /// Ordered [lon, lat] pairs forming the route polyline.
    /// Empty if no shape data is available.
    /// </summary>
    public List<double[]> Shape     { get; set; } = [];
}
