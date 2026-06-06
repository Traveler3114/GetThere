using System.Text.Json;

namespace GetThereShared.Contracts;

public class StopResponse
{
    public string StopId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int RouteType { get; set; } = 3;
}

public class VehicleResponse
{
    public string VehicleId { get; set; } = "";
    public string? TripId { get; set; }
    public string? RouteId { get; set; }
    public string RouteShortName { get; set; } = "";
    public int RouteType { get; set; } = 3;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public float Bearing { get; set; }
    public bool IsRealtime { get; set; }
    public string? BlockId { get; set; }
}

public class RouteResponse
{
    public string RouteId { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string LongName { get; set; } = "";
    public string? Color { get; set; }
    public int RouteType { get; set; }
    public List<double[]> Shape { get; set; } = [];
}

public class MapFeatureResponse
{
    public string Type { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public JsonElement Data { get; set; }
}
