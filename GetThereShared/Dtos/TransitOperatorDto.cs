namespace GetThereShared.Dtos
{
    public class TransitOperatorDto
    {
        public int     Id                   { get; set; }
        public string  Name                 { get; set; } = string.Empty;
        public string? LogoUrl              { get; set; }
        public string? City                 { get; set; }
        public string  Country              { get; set; } = string.Empty;
        public string? GtfsFeedUrl          { get; set; }
        public string? GtfsRealtimeFeedUrl  { get; set; }

        // Feed format & auth — sent to client so it knows how to parse
        public string  RealtimeFeedFormat   { get; set; } = "GTFS_RT_PROTO";
        public string  RealtimeAuthType     { get; set; } = "NONE";
        public string? RealtimeAuthConfig   { get; set; }
        public string? RealtimeAdapterConfig{ get; set; }
    }
}
