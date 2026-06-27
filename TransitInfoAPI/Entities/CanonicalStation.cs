using TransitInfoAPI.Enums;

using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Entities;

// Decision (Task 4.17): Stations relate to each other in exactly two ways:
//   - Fully merged (source deactivated via IsActive=false, recorded in StationMergeLog)
//   - Fully independent
// No "related but separate" linking concept (e.g. parent/child, related stops)
// exists. SupersedesIds is for legacy ID migration only, not reconciliation merges.
public class CanonicalStation
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public StationType StationType { get; set; }
    public RouteType PrimaryRouteType { get; set; }
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

    public ICollection<CanonicalStationOperator> StationOperators { get; set; } = [];
    public ICollection<RawStop> RawStops { get; set; } = [];
}
