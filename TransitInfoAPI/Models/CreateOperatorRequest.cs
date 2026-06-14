namespace TransitInfoAPI.Models;

public class CreateOperatorRequest
{
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string OperatorType { get; set; } = "Transit";
    public string? Website { get; set; }
    public string? GlobalId { get; set; }
}
