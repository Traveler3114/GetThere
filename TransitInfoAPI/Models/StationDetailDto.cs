namespace TransitInfoAPI.Models;

public class StationDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public List<OperatorBriefDto> Operators { get; set; } = [];
    public List<RouteInfoDto> Routes { get; set; } = [];
}

public class OperatorBriefDto
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string OperatorType { get; set; } = string.Empty;
}
