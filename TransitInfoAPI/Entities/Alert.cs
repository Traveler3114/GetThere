namespace TransitInfoAPI.Entities;

public class Alert
{
    public int Id { get; set; }
    public int? FeedId { get; set; }
    public Feed? Feed { get; set; }

    public int? OperatorId { get; set; }
    public Operator? Operator { get; set; }

    public string? HeaderText { get; set; }
    public string? DescriptionText { get; set; }
    public string? Url { get; set; }
    public string? Cause { get; set; }
    public string? Effect { get; set; }
    public DateTime? ActivePeriodStart { get; set; }
    public DateTime? ActivePeriodEnd { get; set; }
    public DateTime FetchedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? AffectedStopIds { get; set; }
    public string? AffectedRouteIds { get; set; }
    public string? AffectedTripIds { get; set; }
    public string? AffectedAgencyIds { get; set; }

    // ── Scraped / HAK source extensions ───────────────────────────────────────
    public string? Kind { get; set; }
    public string? SourceKey { get; set; }
    public string? SourceUrl { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? GeometryGeoJson { get; set; }
    public string? Severity { get; set; }
    public string? MatchedRouteIds { get; set; }
}
