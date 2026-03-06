namespace GetThereShared.Dtos
{
    public class GtfsStopDto
    {
        public string StopId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lon { get; set; }
    }
}