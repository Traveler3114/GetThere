using GetThereShared.Dtos;

namespace GetThere.Services.Realtime;

/// <summary>
/// Parses a raw realtime feed response into a list of vehicle positions.
/// One implementation per feed format (GTFS-RT proto, GTFS-RT JSON, SIRI, REST).
/// </summary>
public interface IRealtimeParser
{
    /// <param name="data">Raw response bytes from the feed URL.</param>
    /// <param name="op">Operator metadata (format, auth, adapter config).</param>
    /// <param name="tripRouteMap">
    ///   Pre-built trip_id → route_id lookup from trips.txt.
    ///   Null if not yet built (parser should degrade gracefully).
    /// </param>
    Task<List<VehiclePositionDto>> ParseAsync(
        byte[] data,
        TransitOperatorDto op,
        Dictionary<string, string>? tripRouteMap);
}
