namespace TransitInfoAPI.Models;

public class OperatorDto
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? CountryName { get; set; }
}
