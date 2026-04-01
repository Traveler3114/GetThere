using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

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

        /// <summary>
        /// How to parse the static feed.
        /// Values: GTFS (default, used by almost every city worldwide)
        /// Future values: NETEX | TRANSXCHANGE
        /// </summary>
        public string StaticFeedFormat { get; set; } = "GTFS";

        // ── Realtime feed ──────────────────────────────────────────────────

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
        /// Auth credentials depending on RealtimeAuthType.
        /// API_KEY_HEADER / BEARER: "HeaderName:Value"
        /// API_KEY_QUERY:           "paramName:Value"
        /// </summary>
        public string? RealtimeAuthConfig { get; set; }

        /// <summary>
        /// For REST adapters: JSON config describing how to map the
        /// operator's proprietary response to our vehicle format.
        /// Null for standard GTFS-RT operators.
        /// </summary>
        public string? RealtimeAdapterConfig { get; set; }

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