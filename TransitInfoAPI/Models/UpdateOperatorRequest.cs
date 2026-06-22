namespace TransitInfoAPI.Models;

public class UpdateOperatorRequest
{
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public int? CountryId { get; set; }
    public string? Website { get; set; }
}
