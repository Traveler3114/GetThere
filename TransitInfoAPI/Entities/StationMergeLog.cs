namespace TransitInfoAPI.Entities;

public class StationMergeLog
{
    public int Id { get; set; }
    public int SourceStationId { get; set; }
    public string SourceStationGlobalId { get; set; } = string.Empty;
    public int TargetStationId { get; set; }
    public int RawStopsMovedCount { get; set; }
    public string? MovedRawStopIds { get; set; }
    public DateTime MergedAt { get; set; } = DateTime.UtcNow;

    public CanonicalStation Source { get; set; } = null!;
    public CanonicalStation Target { get; set; } = null!;
}
