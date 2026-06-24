namespace TransitInfoAPI.Entities;

public class StationMergeMovedRawStop
{
    public int Id { get; set; }
    public int StationMergeLogId { get; set; }
    public int RawStopId { get; set; }

    public StationMergeLog StationMergeLog { get; set; } = null!;
}
