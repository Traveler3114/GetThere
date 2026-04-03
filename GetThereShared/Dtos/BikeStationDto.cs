namespace GetThereShared.Dtos;

/// <summary>
/// Internal DTO representing a single bike-share station.
/// Never sent to the client directly — wrapped in MapFeatureDto at the API boundary.
/// </summary>
public class BikeStationDto
{
    public string StationId      { get; set; } = "";
    public string Name           { get; set; } = "";
    public double Lat            { get; set; }
    public double Lon            { get; set; }
    public int    AvailableBikes { get; set; }
    public int    Capacity       { get; set; }
    public int    ProviderId     { get; set; }
    public string ProviderName   { get; set; } = "";
}
