namespace TransitInfoAPI.Models;

public class VehicleDto
{
    public string VehicleId { get; set; } = string.Empty;
    public string? RouteId { get; set; }
    public string? TripId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Bearing { get; set; }
    public DateTime? LastUpdated { get; set; }
}
