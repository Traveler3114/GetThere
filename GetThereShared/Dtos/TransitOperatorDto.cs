namespace GetThereShared.Dtos
{
    public class TransitOperatorDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string? City { get; set; }
        public string Country { get; set; } = string.Empty;
        public string? GtfsFeedUrl { get; set; }
        public string? GtfsRealtimeFeedUrl { get; set; }
        public bool IsTicketingEnabled { get; set; }
        public bool IsScheduleEnabled { get; set; }
        public bool IsRealtimeEnabled { get; set; }
    }
}