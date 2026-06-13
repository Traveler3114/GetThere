using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CanonicalStation
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public StationType StationType { get; set; }
    public int? ParentStationId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public int? CityId { get; set; }
    public City? City { get; set; }

    public CanonicalStation? ParentStation { get; set; }
    public ICollection<CanonicalStation> ChildStations { get; set; } = [];
    public ICollection<CanonicalStationOperator> StationOperators { get; set; } = [];
}
