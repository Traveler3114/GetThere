namespace TransitInfoAPI.Models;

public class TripDto
{
    public int Id { get; set; }
    public string TripId { get; set; } = string.Empty;
    public string? Headsign { get; set; }
    public string? ShortName { get; set; }
    public int? DirectionId { get; set; }
    public string? RouteName { get; set; }
    public string? RouteType { get; set; }
    public bool ActiveToday { get; set; }
}
