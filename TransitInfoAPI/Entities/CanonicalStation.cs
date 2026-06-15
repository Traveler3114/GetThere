using TransitInfoAPI.Enums;

using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Entities;

public class CanonicalStation
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public StationType StationType { get; set; }
    public RouteType PrimaryRouteType { get; set; }
    public int? ParentStationId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? SupersedesIds { get; set; }
    public Point? Geometry { get; set; }
    public string? AdmCountryCode { get; set; }
    public string? AdmRegionCode { get; set; }

    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public int? CityId { get; set; }
    public City? City { get; set; }

    public int? PlaceId { get; set; }
    public Place? Place { get; set; }

    public CanonicalStation? ParentStation { get; set; }
    public ICollection<CanonicalStation> ChildStations { get; set; } = [];
    public ICollection<CanonicalStationOperator> StationOperators { get; set; } = [];
    public ICollection<RawStop> RawStops { get; set; } = [];
}
