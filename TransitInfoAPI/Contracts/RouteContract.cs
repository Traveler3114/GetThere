namespace TransitInfoAPI.Contracts;

/// <summary>A canonical route (transit line) with operator and type info.</summary>
public class RouteResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }
}

public class RouteInfoResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public string? OperatorGlobalId { get; set; }
}
