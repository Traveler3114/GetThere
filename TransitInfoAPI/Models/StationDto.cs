using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Models;

public class StationDto
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string StationType { get; set; } = string.Empty;
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
}

public class StationOperatorDto
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OperatorType { get; set; } = string.Empty;
    public string? PlatformInfo { get; set; }
}

public class DepartureDto
{
    public string TripId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string Headsign { get; set; } = string.Empty;
    public DateTime? ScheduledDeparture { get; set; }
    public DateTime? EstimatedDeparture { get; set; }
    public int? DelaySeconds { get; set; }
}
