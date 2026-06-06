namespace GetThereShared.Contracts;

public class OperatorResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = string.Empty;
}

public class TransportTypeResponse
{
    public int GtfsRouteType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconFile { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}
