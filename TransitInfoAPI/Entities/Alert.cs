namespace TransitInfoAPI.Entities;

public class Alert
{
    public int Id { get; set; }
    public int FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

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
}
