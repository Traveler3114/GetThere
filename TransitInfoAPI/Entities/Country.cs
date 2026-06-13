namespace TransitInfoAPI.Entities;

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IsoCode { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;

    public ICollection<Region> Regions { get; set; } = [];
    public ICollection<City> Cities { get; set; } = [];
    public ICollection<Operator> Operators { get; set; } = [];
    public ICollection<CanonicalStation> CanonicalStations { get; set; } = [];
    public ICollection<MobilityStation> MobilityStations { get; set; } = [];
}
