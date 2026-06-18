namespace TransitInfoAPI.Models;

public class CreateCountryRequest
{
    public string Name { get; set; } = string.Empty;
    public string IsoCode { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;
}
