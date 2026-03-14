namespace GetThereShared.Dtos;

public class StopDepartureGroupDto
{
    public string RouteId   { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Headsign  { get; set; } = "";
    public List<StopDepartureDto> Departures { get; set; } = [];
}

public class StopDepartureDto
{
    public string  ScheduledTime  { get; set; } = "";
    public string  TripId         { get; set; } = "";
    public string? EstimatedTime  { get; set; }
    public int?    DelayMinutes   { get; set; }
    public bool    IsTracked      { get; set; }
}
