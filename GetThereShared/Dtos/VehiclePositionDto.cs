namespace GetThereShared.Dtos
{
    public class VehiclePositionDto
    {
        public string VehicleId { get; set; } = string.Empty;
        public string? RouteId { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public float Bearing { get; set; }
        public string? Label { get; set; }
    }
}