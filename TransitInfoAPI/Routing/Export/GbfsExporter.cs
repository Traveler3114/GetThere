using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;

namespace TransitInfoAPI.Routing.Export;

/// <summary>
/// Projects the live mobility docks (<c>MobilityStation</c>, already polled by
/// <c>MobilityPollingWorker</c>) into GBFS so OTP can route over bike rental as a mode rather than
/// treat it as a map layer. This is a projection, not new ingestion, and — like the GTFS exporter —
/// it is generic over mobility operators and driven by the same spatial scope, not shaped around
/// Nextbike.
/// <para>
/// It emits the two payloads OTP's bike-rental updater needs, <c>station_information</c> (fixed dock
/// geometry) and <c>station_status</c> (live counts), plus <c>system_information</c>. The
/// auto-discovery <c>gbfs.json</c> is host-dependent, so the serving endpoint builds that from these.
/// </para>
/// </summary>
public sealed class GbfsExporter(TransitDbContext db, IOptions<RoutingOptions> options)
{
    private const string GbfsVersion = "2.3";

    public async Task<GbfsExportResult> ExportAsync(CancellationToken ct = default)
    {
        var scope = options.Value.Scope;

        var stations = (await db.MobilityStations.AsNoTracking()
                .Select(s => new { s.StationId, s.Name, s.Latitude, s.Longitude, s.Capacity, s.AvailableVehicles, s.LastUpdated })
                .ToListAsync(ct))
            .Where(s => GeoBounds.IsUsable(s.Latitude, s.Longitude) && scope.Contains(s.Latitude, s.Longitude))
            .ToList();

        var lastUpdated = ToUnix(stations
            .Select(s => s.LastUpdated)
            .Where(d => d.HasValue)
            .DefaultIfEmpty(DateTime.UtcNow)
            .Max() ?? DateTime.UtcNow);

        var information = new JsonArray();
        var status = new JsonArray();
        foreach (var s in stations)
        {
            // Capacity can be unknown; fall back to the current bike count so docks_available stays
            // non-negative rather than inventing docks.
            var capacity = s.Capacity ?? s.AvailableVehicles;

            information.Add(new JsonObject
            {
                ["station_id"] = s.StationId,
                ["name"] = s.Name,
                ["lat"] = s.Latitude,
                ["lon"] = s.Longitude,
                ["capacity"] = capacity,
            });

            var docksAvailable = Math.Max(0, capacity - s.AvailableVehicles);
            status.Add(new JsonObject
            {
                ["station_id"] = s.StationId,
                ["num_bikes_available"] = s.AvailableVehicles,
                ["num_docks_available"] = docksAvailable,
                ["is_installed"] = 1,
                ["is_renting"] = 1,
                ["is_returning"] = 1,
                ["last_reported"] = ToUnix(s.LastUpdated ?? DateTime.UtcNow),
            });
        }

        return new GbfsExportResult(
            SystemInformation: Wrap(lastUpdated, new JsonObject
            {
                ["system_id"] = "transitinfo",
                ["language"] = "hr",
                ["name"] = "TransitInfo mobility",
                ["timezone"] = string.IsNullOrWhiteSpace(options.Value.Timezone) ? "UTC" : options.Value.Timezone,
            }),
            StationInformation: Wrap(lastUpdated, new JsonObject { ["stations"] = information }),
            StationStatus: Wrap(lastUpdated, new JsonObject { ["stations"] = status }),
            StationCount: stations.Count);
    }

    private static string Wrap(long lastUpdated, JsonObject data)
    {
        var root = new JsonObject
        {
            ["last_updated"] = lastUpdated,
            ["ttl"] = 60,
            ["version"] = GbfsVersion,
            ["data"] = data,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static long ToUnix(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();
}

/// <summary>The GBFS payloads OTP's bike-rental updater consumes, plus a count for logging.</summary>
public sealed record GbfsExportResult(
    string SystemInformation,
    string StationInformation,
    string StationStatus,
    int StationCount);
