namespace GetThereShared.Contracts;

public class StopScheduleResponse
{
    public string StopId { get; set; } = "";
    public string StopName { get; set; } = "";
    public List<DepartureGroupResponse> Groups { get; set; } = [];
}

public class DepartureGroupResponse
{
    public string RouteId { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Headsign { get; set; } = "";
    public List<DepartureResponse> Departures { get; set; } = [];
}

public class DepartureResponse
{
    public string TripId { get; set; } = "";
    public string ScheduledTime { get; set; } = "";
    public string? EstimatedTime { get; set; }
    public int? DelayMinutes { get; set; }
    public bool IsRealtime { get; set; }
}

public class TripDetailResponse
{
    public string TripId { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Headsign { get; set; } = "";
    public int RouteType { get; set; } = 3;
    public List<TripStopResponse> Stops { get; set; } = [];
    public int CurrentStopIndex { get; set; }
    public double VehicleLat { get; set; }
    public double VehicleLon { get; set; }
    public bool IsRealtime { get; set; }
    public string? VehicleId { get; set; }
    public string? BlockId { get; set; }
}

public class TripStopResponse
{
    public int Sequence { get; set; }
    public string StopId { get; set; } = "";
    public string StopName { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string ScheduledTime { get; set; } = "";
    public string? EstimatedTime { get; set; }
    public int? DelayMinutes { get; set; }
    public bool IsPassed { get; set; }
}
