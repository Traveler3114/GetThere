using GetThereAPI.Entities;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Contract every realtime parser must fulfil.
/// Takes raw feed bytes, returns a list of ParsedVehicle.
///
/// Uses TransitOperator (entity) not OperatorDto — parsers are
/// server-side only and need access to auth config and adapter config
/// which are intentionally excluded from OperatorDto.
/// </summary>
public interface IRealtimeParser
{
    /// <param name="data">Raw bytes from the realtime feed URL.</param>
    /// <param name="op">Full operator entity including auth and adapter config.</param>
    /// <param name="tripRouteMap">
    /// trip_id → route_id lookup built from trips.txt.
    /// Used when the feed doesn't include route_id directly.
    /// Null if static data hasn't loaded yet — parsers degrade gracefully.
    /// </param>
    Task<List<ParsedVehicle>> ParseAsync(
        byte[] data,
        TransitOperator op,
        Dictionary<string, string>? tripRouteMap);
}
