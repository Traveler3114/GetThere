namespace OpenTripPlannerAPI.Core;

public sealed class StopTimeUpdateData
{
    public string StopId { get; set; } = string.Empty;
    public int StopSequence { get; set; }
    public int DelaySec { get; set; }
    public int ScheduledArrivalSec { get; set; }
    public int ScheduledDepartureSec { get; set; }
    public string? TripStartDate { get; set; }
}
