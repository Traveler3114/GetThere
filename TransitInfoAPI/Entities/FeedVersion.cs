using TransitInfoAPI.Enums;

using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Entities;

public class FeedVersion
{
    public int Id { get; set; }
    public int FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

    public string Sha1 { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public bool IsActive { get; set; }
    public FeedImportStatus ImportStatus { get; set; }
    public string? ImportError { get; set; }
    public DateTime? LastModified { get; set; }
    public string? ETag { get; set; }

    public Geometry? ConvexHull { get; set; }

    public DateOnly? ServiceLevelStart { get; set; }
    public DateOnly? ServiceLevelEnd { get; set; }

    public int StopCount { get; set; }
    public int RouteCount { get; set; }
    public int TripCount { get; set; }
    public int AgencyCount { get; set; }

    public ICollection<Agency> Agencies { get; set; } = [];
    public ICollection<RawStop> RawStops { get; set; } = [];
}
