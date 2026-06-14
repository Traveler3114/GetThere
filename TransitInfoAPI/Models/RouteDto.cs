namespace TransitInfoAPI.Models;

public class RouteDto
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }
}
