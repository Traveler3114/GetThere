namespace GetThereAPI.Entities
{
    public class TransitOperator
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }

        public string TicketApiBaseUrl { get; set; } = string.Empty;
        public string TicketApiKey { get; set; } = string.Empty;

        // ── Static feed ────────────────────────────────────────────────────

        public string? GtfsFeedUrl { get; set; }

        // ── Realtime feed ──────────────────────────────────────────────────

        public string? GtfsRealtimeFeedUrl { get; set; }

        // ── Metadata ───────────────────────────────────────────────────────

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        public int? CityId { get; set; }
        public City? City { get; set; }


        // Navigation: Transport types this operator runs (tram, bus, train etc.)
        public ICollection<TransportType> TransportTypes { get; set; } = new List<TransportType>();
    }
}
