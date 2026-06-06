namespace GetThereShared.Contracts;

public class BikeStationResponse
{
    public string StationId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int AvailableBikes { get; set; }
    public int Capacity { get; set; }
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = "";
    public string CountryName { get; set; } = "";
}
