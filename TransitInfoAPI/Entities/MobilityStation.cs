namespace TransitInfoAPI.Entities;

public class MobilityStation
{
    public int Id { get; set; }
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? Capacity { get; set; }
    public int AvailableVehicles { get; set; }
    public DateTime? LastUpdated { get; set; }

    public int MobilityProviderId { get; set; }
    public MobilityProvider MobilityProvider { get; set; } = null!;

    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;
}
