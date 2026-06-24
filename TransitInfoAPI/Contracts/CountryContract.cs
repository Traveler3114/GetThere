using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

public class CountryResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IsoCode { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;
}

public class CreateCountryRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(10)] public string IsoCode { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Continent { get; set; } = string.Empty;
}
