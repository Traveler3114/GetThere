namespace TransitInfoAPI.Models;

public class StationMergeLogDto
{
    public int Id { get; set; }
    public int SourceStationId { get; set; }
    public string SourceStationName { get; set; } = string.Empty;
    public string SourceStationGlobalId { get; set; } = string.Empty;
    public int TargetStationId { get; set; }
    public string TargetStationName { get; set; } = string.Empty;
    public int RawStopsMovedCount { get; set; }
    public DateTime MergedAt { get; set; }
    public bool Unmerged { get; set; }
}
