namespace TransitInfoAPI.Models;

public class FeedVersionDto
{
    public int Id { get; set; }
    public int FeedId { get; set; }
    public string Sha1 { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public bool IsActive { get; set; }
    public string ImportStatus { get; set; } = string.Empty;
    public string? ImportError { get; set; }
    public DateOnly? ServiceLevelStart { get; set; }
    public DateOnly? ServiceLevelEnd { get; set; }
    public int StopCount { get; set; }
    public int RouteCount { get; set; }
    public int TripCount { get; set; }
}
