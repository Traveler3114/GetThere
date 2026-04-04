namespace GetThereShared.Dtos;

/// <summary>
/// DTO representing a single bike-share station.
/// Returned directly via GET /map/bike-stations and also wrapped in MapFeatureDto
/// inside the unified GET /map/features endpoint.
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
