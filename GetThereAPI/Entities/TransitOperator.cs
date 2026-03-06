namespace GetThereAPI.Entities
{
    public class TransitOperator
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }

        // Existing ticket API fields
        public string TicketApiBaseUrl { get; set; } = string.Empty;
        public string TicketApiKey { get; set; } = string.Empty;

        // ── NEW: GTFS Schedule ─────────────────────────────────────────────
        // The static GTFS feed download URL (from Mobility Database)
        public string? GtfsFeedUrl { get; set; }

        // ── NEW: GTFS Realtime ─────────────────────────────────────────────
        // GTFS-RT vehicle positions feed URL
        public string? GtfsRealtimeFeedUrl { get; set; }

        // ── NEW: Feature flags ─────────────────────────────────────────────
        public bool IsTicketingEnabled { get; set; } = false;
        public bool IsScheduleEnabled { get; set; } = false;
        public bool IsRealtimeEnabled { get; set; } = false;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Every operator must have a country
        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        // Some are city-specific (nullable for e.g. national trains)
        public int? CityId { get; set; }
        public City? City { get; set; }
    }
}