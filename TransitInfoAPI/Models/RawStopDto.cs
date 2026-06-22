namespace TransitInfoAPI.Models;

public class RawStopDto
{
    public int Id { get; set; }
    public string RawStopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string StationType { get; set; } = string.Empty;
    public string RouteType { get; set; } = string.Empty;
    public int? CanonicalStationId { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
}
