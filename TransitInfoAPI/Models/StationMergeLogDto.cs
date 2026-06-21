namespace TransitInfoAPI.Models;

public class StationMergeLogDto
{
    public int Id { get; set; }
    public int SourceStationId { get; set; }
    public string SourceStationGlobalId { get; set; } = string.Empty;
    public int TargetStationId { get; set; }
    public int RawStopsMovedCount { get; set; }
    public DateTime MergedAt { get; set; }
}
