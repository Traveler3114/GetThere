using GetThereShared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Services.Realtime;

/// <summary>
/// Parses proprietary REST/JSON feeds using a field-mapping config stored in the DB.
///
/// RealtimeAdapterConfig JSON schema:
/// {
///   "arrayPath":   "data.vehicles",   // dot-path to the array of vehicles (empty = root array)
///   "lat":         "lat",             // field name for latitude
///   "lon":         "lng",             // field name for longitude
///   "bearing":     "heading",         // field name for bearing (optional)
///   "vehicleId":   "vehicle_id",      // field name for vehicle id
///   "routeId":     "route_number",    // field name for route id (optional)
///   "label":       "display_name"     // field name for display label (optional)
/// }
///
/// Example operators: some city APIs, OpenSky for aircraft, etc.
/// </summary>
public class RestJsonParser : IRealtimeParser
{
    public Task<List<VehiclePositionDto>> ParseAsync(
        byte[] data,
        TransitOperatorDto op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<VehiclePositionDto>();

        if (string.IsNullOrEmpty(op.RealtimeAdapterConfig))
        {
            Trace.WriteLine($"[REST:{op.Name}] No RealtimeAdapterConfig set — cannot parse");
            return Task.FromResult(result);
        }

        try
        {
            var cfg = JsonDocument.Parse(op.RealtimeAdapterConfig).RootElement;
            var root = JsonDocument.Parse(data).RootElement;

            // Navigate to the vehicles array via dot-path
            var arrayPath = cfg.TryGetProperty("arrayPath", out var ap) ? ap.GetString() : null;
            var array = ResolveJsonPath(root, arrayPath);
            if (array.ValueKind != JsonValueKind.Array)
            {
                Trace.WriteLine($"[REST:{op.Name}] Could not find vehicle array at path '{arrayPath}'");
                return Task.FromResult(result);
            }

            // Field name mappings
            string fLat = cfg.TryGetProperty("lat", out var v) ? v.GetString()! : "lat";
            string fLon = cfg.TryGetProperty("lon", out v) ? v.GetString()! : "lon";
            string fBearing = cfg.TryGetProperty("bearing", out v) ? v.GetString()! : "bearing";
            string fVehicleId = cfg.TryGetProperty("vehicleId", out v) ? v.GetString()! : "id";
            string fRouteId = cfg.TryGetProperty("routeId", out v) ? v.GetString()! : "routeId";
            string fLabel = cfg.TryGetProperty("label", out v) ? v.GetString()! : "label";

            foreach (var item in array.EnumerateArray())
            {
                var dto = new VehiclePositionDto
                {
                    Lat = GetDouble(item, fLat),
                    Lon = GetDouble(item, fLon),
                    Bearing = (float)GetDouble(item, fBearing),
                    VehicleId = GetString(item, fVehicleId) ?? Guid.NewGuid().ToString(),
                    RouteId = GetString(item, fRouteId),
                    Label = GetString(item, fLabel),
                };

                if (dto.Lat == 0 && dto.Lon == 0) continue;

                // Trip→route fallback
                if (string.IsNullOrEmpty(dto.RouteId) && tripRouteMap != null)
                {
                    var tripId = GetString(item, "trip_id") ?? GetString(item, "tripId");
                    if (tripId != null && tripRouteMap.TryGetValue(tripId, out var mappedRoute))
                        dto.RouteId = mappedRoute;
                }

                result.Add(dto);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[REST:{op.Name}] Parse error: {ex.Message}");
        }

        Trace.WriteLine($"[REST:{op.Name}] {result.Count} vehicles");
        return Task.FromResult(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// Resolves a dot-separated path like "data.vehicles" into a JsonElement.
    private static JsonElement ResolveJsonPath(JsonElement root, string? path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return default;
            if (!current.TryGetProperty(part, out current)) return default;
        }
        return current;
    }

    private static double GetDouble(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var prop)) return 0;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDouble(),
            JsonValueKind.String => double.TryParse(prop.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0,
            _ => 0
        };
    }

    private static string? GetString(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString()
             : prop.ValueKind == JsonValueKind.Number ? prop.GetRawText()
             : null;
    }
}