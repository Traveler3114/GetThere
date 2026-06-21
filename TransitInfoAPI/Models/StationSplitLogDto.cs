namespace TransitInfoAPI.Models;

public class StationSplitLogDto
{
    public int Id { get; set; }
    public int RawStopId { get; set; }
    public int FeedVersionId { get; set; }
    public int CandidateStationId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}
