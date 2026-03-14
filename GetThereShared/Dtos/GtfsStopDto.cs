namespace GetThereShared.Dtos;

public class GtfsStopDto
{
    public string StopId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    /// <summary>
    /// GTFS route_type of the primary service at this stop.
    /// 0 = tram, 3 = bus (default). Populated from the cached
    /// stop_route_types.json built during InstallAsync.
    /// </summary>
    public int RouteType { get; set; } = 3;
}