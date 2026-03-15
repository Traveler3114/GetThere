namespace GetThereShared.Dtos;

public class GtfsStopDto
{
    public string StopId   { get; set; } = "";
    public string Name     { get; set; } = "";
    public double Lat      { get; set; }
    public double Lon      { get; set; }
    public int    RouteType { get; set; } = 3; // 0=tram, 3=bus default
}
