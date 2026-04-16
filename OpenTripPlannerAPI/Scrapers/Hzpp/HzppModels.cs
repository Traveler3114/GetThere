namespace OpenTripPlannerAPI.Scrapers.Hzpp;

public class GtfsData
{
    public Dictionary<string, string> StopsById { get; set; } = new();
    public Dictionary<string, string> StopIdByName { get; set; } = new();
    public Dictionary<string, TripInfo> TripsById { get; set; } = new();
    public Dictionary<string, List<string>> TripsByTrain { get; set; } = new();
    public Dictionary<string, List<StopTime>> StopTimes { get; set; } = new();
    public Dictionary<string, HashSet<DateOnly>> Calendar { get; set; } = new();
}

public class TripInfo
{
    public string TripId { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string TrainNumber { get; set; } = "";
}

public class StopTime
{
    public string StopId { get; set; } = "";
    public int StopSequence { get; set; }
    public int ArrivalSec { get; set; }
    public int DepartureSec { get; set; }
}

public class TrainPayload
{
    public string TrainNumber { get; set; } = "";
    public string? CurrentStation { get; set; }
    public int DelayMin { get; set; }
    public bool Finished { get; set; }
    public string? Route { get; set; }
}

public class StopTimeUpdateDto
{
    public string StopId { get; set; } = "";
    public int StopSequence { get; set; }
    public int DelaySec { get; set; }
    public int ScheduledArrivalSec { get; set; }
    public int ScheduledDepartureSec { get; set; }
}

public sealed class HzppScraperOptions
{
    public bool Enabled { get; set; } = true;
    public string FeedId { get; set; } = "hzpp";
    public int IntervalSeconds { get; set; } = 30;
    public double RequestDelaySeconds { get; set; } = 0.3;
    public string GtfsZipUrl { get; set; } = "https://www.hzpp.hr/GTFS_files.zip";
}
