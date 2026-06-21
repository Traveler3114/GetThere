namespace TransitInfoAPI.Entities;

public class StationSplitLog
{
    public int Id { get; set; }
    public int RawStopId { get; set; }
    public int FeedVersionId { get; set; }
    public int CandidateStationId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
