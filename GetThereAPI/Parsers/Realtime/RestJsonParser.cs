using GetThereAPI.Entities;
using System.Diagnostics;
using System.Text.Json;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Parses proprietary REST/JSON feeds using a field-mapping config
/// stored in TransitOperator.RealtimeAdapterConfig.
///
/// RealtimeAdapterConfig JSON schema:
/// {
///   "arrayPath": "data.vehicles",  // dot-path to vehicle array (empty = root)
///   "lat":       "lat",            // field name for latitude
///   "lon":       "lng",            // field name for longitude
///   "bearing":   "heading",        // field name for bearing (optional)
///   "vehicleId": "vehicle_id",     // field name for vehicle id
///   "routeId":   "route_number",   // field name for route id (optional)
///   "label":     "display_name"    // field name for display label (optional)
/// }
/// </summary>
public class RestJsonParser : IRealtimeParser
{
    public Task<List<ParsedVehicle>> ParseAsync(
        byte[] data,
        TransitOperator op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<ParsedVehicle>();

        if (string.IsNullOrEmpty(op.RealtimeAdapterConfig))
        {
            Trace.WriteLine($"[REST:{op.Name}] No RealtimeAdapterConfig set — cannot parse");
            return Task.FromResult(result);
        }

        try
        {
            var cfg  = JsonDocument.Parse(op.RealtimeAdapterConfig).RootElement;
            var root = JsonDocument.Parse(data).RootElement;

            // Navigate to the vehicle array via dot-path
            var arrayPath = cfg.TryGetProperty("arrayPath", out var ap) ? ap.GetString() : null;
            var array     = ResolveJsonPath(root, arrayPath);
            if (array.ValueKind != JsonValueKind.Array)
            {
                Trace.WriteLine($"[REST:{op.Name}] Vehicle array not found at '{arrayPath}'");
                return Task.FromResult(result);
            }

            // Field name mappings from config
            string fLat       = Str(cfg, "lat",       "lat");
            string fLon       = Str(cfg, "lon",       "lon");
            string fBearing   = Str(cfg, "bearing",   "bearing");
            string fVehicleId = Str(cfg, "vehicleId", "id");
            string fRouteId   = Str(cfg, "routeId",   "routeId");
            string fLabel     = Str(cfg, "label",     "label");

            foreach (var item in array.EnumerateArray())
            {
                var lat = GetDouble(item, fLat);
                var lon = GetDouble(item, fLon);
                if (lat == 0 && lon == 0) continue;

                var routeId = GetString(item, fRouteId);

                // Trip→route fallback if routeId not in feed
                if (string.IsNullOrEmpty(routeId) && tripRouteMap != null)
                {
                    var tripId = GetString(item, "trip_id") ?? GetString(item, "tripId");
                    if (tripId != null)
                        tripRouteMap.TryGetValue(tripId, out routeId);
                }

                result.Add(new ParsedVehicle
                {
                    VehicleId = GetString(item, fVehicleId) ?? Guid.NewGuid().ToString(),
                    RouteId   = routeId,
                    Label     = GetString(item, fLabel),
                    Lat       = lat,
                    Lon       = lon,
                    Bearing   = (float)GetDouble(item, fBearing),
                });
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

    private static string Str(JsonElement cfg, string key, string fallback)
        => cfg.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;

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
