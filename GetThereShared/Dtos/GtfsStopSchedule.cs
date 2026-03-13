namespace GetThereShared.Dtos
{
    public class GtfsStopSchedule
    {
        public string TripId { get; set; } = string.Empty;
        public string ArrivalTime { get; set; } = string.Empty;
        public string DepartureTime { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string StopId { get; set; } = string.Empty;
    }
}