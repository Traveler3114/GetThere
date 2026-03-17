using GetThereAPI.Entities;
using System.Diagnostics;
using System.Text.Json;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Parses GTFS-RT feeds served as JSON instead of protobuf.
/// The JSON structure mirrors the protobuf schema exactly.
/// Example: https://api.winnipegtransit.com/v3/vehicle-locations.json
/// </summary>
public class GtfsRtJsonParser : IRealtimeParser
{
    public Task<List<ParsedVehicle>> ParseAsync(
        byte[] data,
        TransitOperator op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<ParsedVehicle>();

        try
        {
            var root = JsonDocument.Parse(data).RootElement;
            if (!root.TryGetProperty("entity", out var entities))
                return Task.FromResult(result);

            foreach (var entity in entities.EnumerateArray())
            {
                JsonElement vp;
                bool found = entity.TryGetProperty("vehicle", out vp)
                          || entity.TryGetProperty("vehiclePosition", out vp);
                if (!found) continue;

                var vehicle = new ParsedVehicle();

                // Position
                if (vp.TryGetProperty("position", out var pos))
                {
                    vehicle.Lat = pos.TryGetProperty("latitude", out var lat) ? lat.GetDouble() : 0;
                    vehicle.Lon = pos.TryGetProperty("longitude", out var lon) ? lon.GetDouble() : 0;
                    vehicle.Bearing = pos.TryGetProperty("bearing", out var brg) ? (float)brg.GetDouble() : 0;
                }

                if (vehicle.Lat == 0 && vehicle.Lon == 0) continue;

                // VehicleDescriptor
                if (vp.TryGetProperty("vehicle", out var vd))
                {
                    vehicle.VehicleId = vd.TryGetProperty("id", out var vid) ? vid.GetString() ?? "" : "";
                    vehicle.Label = vd.TryGetProperty("label", out var vlbl) ? vlbl.GetString() : null;
                }

                // TripDescriptor
                if (vp.TryGetProperty("trip", out var trip))
                {
                    // Prefer explicit route_id from feed
                    if (trip.TryGetProperty("routeId", out var rid) ||
                        trip.TryGetProperty("route_id", out rid))
                        vehicle.RouteId = rid.GetString();

                    // Get trip_id
                    var tripId = trip.TryGetProperty("tripId", out var tid) ||
                                 trip.TryGetProperty("trip_id", out tid)
                        ? tid.GetString() : null;

                    if (tripId != null)
                    {
                        vehicle.TripId = tripId;

                        // Fall back to trip map for route_id
                        if (string.IsNullOrEmpty(vehicle.RouteId) && tripRouteMap != null)
                        {
                            // Use local variable — can't use property as out parameter
                            if (tripRouteMap.TryGetValue(tripId, out var mappedRouteId))
                                vehicle.RouteId = mappedRouteId;
                        }
                    }
                }

                // Entity-level id as last-resort vehicle id
                if (string.IsNullOrEmpty(vehicle.VehicleId))
                    vehicle.VehicleId = entity.TryGetProperty("id", out var eid)
                        ? eid.GetString() ?? "" : "";

                result.Add(vehicle);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[GtfsRtJson:{op.Name}] Parse error: {ex.Message}");
        }

        Trace.WriteLine($"[GtfsRtJson:{op.Name}] {result.Count} vehicles");
        return Task.FromResult(result);
    }
}