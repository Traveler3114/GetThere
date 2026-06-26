namespace TransitInfoAPI.Contracts;

public class MobilityStationResponse
{
    public int Id { get; set; }
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int AvailableVehicles { get; set; }
    public int? Capacity { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
    public string? CityName { get; set; }
}
