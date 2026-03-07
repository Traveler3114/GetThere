namespace GetThereAPI.Entities
{
    public class TransitOperator
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }

        public string TicketApiBaseUrl { get; set; } = string.Empty;
        public string TicketApiKey { get; set; } = string.Empty;

        // ── GTFS Static ────────────────────────────────────────────────────
        public string? GtfsFeedUrl { get; set; }

        // ── GTFS Realtime ──────────────────────────────────────────────────
        public string? GtfsRealtimeFeedUrl { get; set; }

        /// <summary>
        /// How to parse the realtime feed.
        /// Values: GTFS_RT_PROTO | GTFS_RT_JSON | SIRI | REST
        /// </summary>
        public string RealtimeFeedFormat { get; set; } = "GTFS_RT_PROTO";

        /// <summary>
        /// How to authenticate against the realtime feed.
        /// Values: NONE | API_KEY_HEADER | API_KEY_QUERY | BEARER
        /// </summary>
        public string RealtimeAuthType { get; set; } = "NONE";

        /// <summary>
        /// The key, token, or header name depending on RealtimeAuthType.
        /// For API_KEY_HEADER / BEARER: "HeaderName:Value"
        /// For API_KEY_QUERY: "paramName:Value"
        /// </summary>
        public string? RealtimeAuthConfig { get; set; }

        /// <summary>
        /// For REST adapters: JSON template describing how to map the
        /// operator's proprietary response to VehiclePositionDto.
        /// Null for standard GTFS-RT operators.
        /// </summary>
        public string? RealtimeAdapterConfig { get; set; }

        // ── Feature flags ──────────────────────────────────────────────────
        public bool IsTicketingEnabled { get; set; } = false;
        public bool IsScheduleEnabled  { get; set; } = false;
        public bool IsRealtimeEnabled  { get; set; } = false;
        public bool IsActive           { get; set; } = true;
        public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;

        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        public int? CityId { get; set; }
        public City? City { get; set; }
    }
}
