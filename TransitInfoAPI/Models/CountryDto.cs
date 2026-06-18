namespace TransitInfoAPI.Models;

public class CountryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IsoCode { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;
}
