namespace TransitInfoAPI.Entities;

public class Operator
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? SupersedesIds { get; set; }
    public string? WikidataId { get; set; }
    public string? Tags { get; set; }
    public string? AssociatedFeeds { get; set; }

    /// <summary>
    /// The operator's service area, for stop-coordinate resolution. A name-only timetable — a PDF or
    /// HTML page with no lat/lon — is placed by matching its stop names against the stop gazetteer
    /// within this region; the centroid plus <see cref="RegionRadiusKm"/> is the search box.
    /// Coordinate-complete operators leave all four null and skip resolution entirely.
    /// </summary>
    public double? RegionCentroidLat { get; set; }
    public double? RegionCentroidLon { get; set; }
    public double? RegionRadiusKm { get; set; }
    public string? RegionName { get; set; }

    public ICollection<Feed> Feeds { get; set; } = [];
    public ICollection<Agency> Agencies { get; set; } = [];
    public ICollection<CanonicalRoute> Routes { get; set; } = [];
    public ICollection<CanonicalStationOperator> StationOperators { get; set; } = [];
}
