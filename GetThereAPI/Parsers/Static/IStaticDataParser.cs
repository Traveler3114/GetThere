using GetThereShared.Dtos;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Contract every static data parser must fulfil.
///
/// A static parser takes raw feed bytes and returns stops, routes,
/// and trip data in our unified DTO format.
///
/// Adding a new format (NeTEx, TransXChange etc.) means:
///   1. Create NewFormatStaticParser : IStaticDataParser
///   2. Add one line to StaticParserFactory
///   3. StaticDataManager needs zero changes
/// </summary>
public interface IStaticDataParser
{
    Task<List<StopDto>>    ParseStopsAsync(byte[] data);
    Task<List<RouteDto>>   ParseRoutesAsync(byte[] data);

    /// <summary>trip_id → route_id. Used by RealtimeManager.</summary>
    Task<Dictionary<string, string>> ParseTripRouteMapAsync(byte[] data);

    /// <summary>
    /// trip_id → (routeId, headsign) for active services on a given date.
    /// Null serviceIds = all trips regardless of service.
    /// Used by ScheduleManager.
    /// </summary>
    Task<Dictionary<string, (string RouteId, string Headsign)>> ParseTripInfoMapAsync(
        byte[] data, HashSet<string>? serviceIds);

    /// <summary>
    /// Service IDs running on a given date (calendar.txt + calendar_dates.txt).
    /// </summary>
    Task<HashSet<string>> GetActiveServiceIdsAsync(byte[] data, DateOnly date);

    /// <summary>Departures for one stop on one date, grouped by route+direction.</summary>
    Task<List<DepartureGroupDto>> ParseStopScheduleAsync(
        byte[] data, string stopId, DateOnly date);

    /// <summary>Full ordered stop sequence for one trip.</summary>
    Task<List<TripStopDto>> ParseTripStopsAsync(byte[] data, string tripId);

    /// <summary>Distinct route types present in this feed.</summary>
    Task<HashSet<int>> ParseUsedRouteTypesAsync(byte[] data);
}
