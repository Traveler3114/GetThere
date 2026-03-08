using GetThereShared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Services.Realtime;

/// <summary>
/// Parses GTFS-RT feeds served as JSON instead of protobuf.
/// The JSON structure mirrors the protobuf schema exactly.
/// Example: https://api.winnipegtransit.com/v3/vehicle-locations.json
/// </summary>
public class GtfsRtJsonParser : IRealtimeParser
{
    public Task<List<VehiclePositionDto>> ParseAsync(
        byte[] data,
        TransitOperatorDto op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<VehiclePositionDto>();

        try
        {
            var root = JsonDocument.Parse(data).RootElement;
            if (!root.TryGetProperty("entity", out var entities)) return Task.FromResult(result);

            foreach (var entity in entities.EnumerateArray())
            {
                // Standard field name is "vehicle" — some feeds use "vehiclePosition"
                JsonElement vp;
                bool found = entity.TryGetProperty("vehicle", out vp)
                          || entity.TryGetProperty("vehiclePosition", out vp);
                if (!found) continue;

                var dto = new VehiclePositionDto();

                // Position
                if (vp.TryGetProperty("position", out var pos))
                {
                    dto.Lat     = pos.TryGetProperty("latitude",  out var lat) ? lat.GetDouble() : 0;
                    dto.Lon     = pos.TryGetProperty("longitude", out var lon) ? lon.GetDouble() : 0;
                    dto.Bearing = pos.TryGetProperty("bearing",   out var brg) ? (float)brg.GetDouble() : 0;
                }

                if (dto.Lat == 0 && dto.Lon == 0) continue;

                // VehicleDescriptor
                if (vp.TryGetProperty("vehicle", out var vd))
                {
                    dto.VehicleId = vd.TryGetProperty("id",    out var vid)   ? vid.GetString() ?? "" : "";
                    dto.Label     = vd.TryGetProperty("label", out var vlbl)  ? vlbl.GetString()      : null;
                }

                // TripDescriptor
                if (vp.TryGetProperty("trip", out var trip))
                {
                    // Prefer explicit route_id from feed
                    if (trip.TryGetProperty("routeId", out var rid) || trip.TryGetProperty("route_id", out rid))
                        dto.RouteId = rid.GetString();

                    // Fall back to trip map
                    if (string.IsNullOrEmpty(dto.RouteId))
                    {
                        var tripId = trip.TryGetProperty("tripId",  out var tid)
                                  || trip.TryGetProperty("trip_id", out tid)
                            ? tid.GetString() : null;

                        if (tripId != null && tripRouteMap != null)
                            tripRouteMap.TryGetValue(tripId, out var mapped);
                    }
                }

                // Entity-level id as last-resort vehicle id
                if (string.IsNullOrEmpty(dto.VehicleId))
                    dto.VehicleId = entity.TryGetProperty("id", out var eid) ? eid.GetString() ?? "" : "";

                result.Add(dto);
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
