namespace TransitInfoAPI.Entities;

public class Place
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AdmCountryCode { get; set; } = string.Empty;
    public string? AdmRegionCode { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public long? Population { get; set; }

    public ICollection<CanonicalStation> Stations { get; set; } = [];
}
