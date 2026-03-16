namespace GetThereShared.Dtos
{
    public class GtfsRouteDto
    {
        public string RouteId { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public string LongName { get; set; } = string.Empty;
        public string? Color { get; set; } = "1a73e8";
        public int RouteType { get; set; }
        public List<double[]> Shape { get; set; } = new();
    }
}