using NetTopologySuite.Geometries;

using TransitInfoAPI.Enums;

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

    /// <summary>
    /// How much of a network this version carries. Every GTFS archive is
    /// <see cref="FeedCompleteness.Schedule"/>; custom sources may legitimately be less.
    /// </summary>
    public FeedCompleteness Completeness { get; set; } = FeedCompleteness.Schedule;

    /// <summary>
    /// Comma-separated sections a completer invented rather than read from the source, so the admin
    /// console can distinguish "this operator publishes no calendar" from "this operator publishes a
    /// calendar and it is empty".
    /// </summary>
    public string? SynthesizedSections { get; set; }

    public ICollection<Agency> Agencies { get; set; } = [];
    public ICollection<RawStop> RawStops { get; set; } = [];
}
