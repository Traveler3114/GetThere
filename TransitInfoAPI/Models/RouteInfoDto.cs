namespace TransitInfoAPI.Models;

public class RouteInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public string? OperatorGlobalId { get; set; }
}
