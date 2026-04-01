namespace GetThereAPI.Entities;

public class TransportType
{
    public int Id { get; set; }
    public int GtfsRouteType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconFile { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    // Navigation
    public ICollection<TransitOperator> Operators { get; set; } = new List<TransitOperator>();
}