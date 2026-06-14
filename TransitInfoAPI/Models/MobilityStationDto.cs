namespace TransitInfoAPI.Models;

public class MobilityStationDto
{
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int AvailableVehicles { get; set; }
    public int Capacity { get; set; }
    public string ProviderName { get; set; } = string.Empty;
}
