using System.Globalization;
using System.Text.Json;
using GetThereShared.Dtos;

namespace GetThereAPI.Transit;

public class OtpTransitProvider : ITransitProvider
{
    private const string UnknownRouteIdFallback = "unknown";
    private readonly OtpClient _otpClient;

    public OtpTransitProvider(OtpClient otpClient)
    {
        _otpClient = otpClient;
    }

    public async Task<List<StopDto>> GetStopsAsync(string instanceKey, CancellationToken ct = default)
    {
        const string queryWithRoutes = """
            query Stops {
              stops {
                gtfsId
                name
                lat
                lon
                routes {
                  mode
                }
              }
            }
            """;

        const string queryFallback = """
            query Stops {
              stops {
                gtfsId
                name
                lat
                lon
              }
            }
            """;

        var doc = await _otpClient.QueryAsync(instanceKey, queryWithRoutes, ct: ct)
                  ?? await _otpClient.QueryAsync(instanceKey, queryFallback, ct: ct);
        if (doc is null) return [];

        if (!TryGetDataNode(doc, "stops", out var stopsNode)
            || stopsNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var stops = new List<StopDto>();
        foreach (var item in stopsNode.EnumerateArray())
        {
            var stopId = GetString(item, "gtfsId");
            var name = GetString(item, "name");
            var lat = GetDouble(item, "lat");
            var lon = GetDouble(item, "lon");

            if (string.IsNullOrWhiteSpace(stopId) || string.IsNullOrWhiteSpace(name))
                continue;

            stops.Add(new StopDto
            {
                StopId = stopId,
                Name = name,
                Lat = lat,
                Lon = lon,
                RouteType = ResolveStopRouteType(item)
            });
        }

        return stops;
    }

    public async Task<List<RouteDto>> GetRoutesAsync(string instanceKey, CancellationToken ct = default)
    {
        const string query = """
            query Routes {
              routes {
                gtfsId
                shortName
                longName
                color
                mode
              }
            }
            """;

        var doc = await _otpClient.QueryAsync(instanceKey, query, ct: ct);
        if (doc is null) return [];

        if (!TryGetDataNode(doc, "routes", out var routesNode)
            || routesNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var routes = new List<RouteDto>();
        foreach (var item in routesNode.EnumerateArray())
        {
            var routeId = GetString(item, "gtfsId");
            if (string.IsNullOrWhiteSpace(routeId))
                continue;

            var shortName = GetString(item, "shortName");
            var longName = GetString(item, "longName");
            var mode = GetString(item, "mode");

            routes.Add(new RouteDto
            {
                RouteId = routeId,
                ShortName = string.IsNullOrWhiteSpace(shortName) ? "Route" : shortName,
                LongName = longName,
                Color = NormalizeColor(GetString(item, "color")),
                RouteType = MapModeToRouteType(mode),
                Shape = []
            });
        }

        return routes;
    }

    public async Task<StopScheduleDto?> GetStopScheduleAsync(
        string instanceKey,
        string stopId,
        CancellationToken ct = default)
    {
        const string query = """
            query StopSchedule($id: String!, $count: Int!, $range: Int!) {
              stop(id: $id) {
                gtfsId
                name
                stoptimesWithoutPatterns(numberOfDepartures: $count, timeRange: $range) {
                  scheduledDeparture
                  realtimeDeparture
                  realtime
                  serviceDay
                  trip {
                    gtfsId
                    tripHeadsign
                    route {
                      gtfsId
                      shortName
                    }
                  }
                }
              }
            }
            """;

        var variables = new
        {
            id = stopId,
            count = 40,
            range = 86400
        };

        var doc = await _otpClient.QueryAsync(instanceKey, query, variables, ct);
        if (doc is null) return null;

        if (!TryGetDataNode(doc, "stop", out var stopNode)
            || stopNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new StopScheduleDto
        {
            StopId = GetString(stopNode, "gtfsId"),
            StopName = GetString(stopNode, "name"),
            Groups = []
        };

        if (string.IsNullOrWhiteSpace(result.StopId))
            result.StopId = stopId;
        if (string.IsNullOrWhiteSpace(result.StopName))
            result.StopName = "Unknown Stop";

        if (!stopNode.TryGetProperty("stoptimesWithoutPatterns", out var timesNode)
            || timesNode.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var grouped = new Dictionary<string, DepartureGroupDto>(StringComparer.Ordinal);

        foreach (var item in timesNode.EnumerateArray())
        {
            if (!item.TryGetProperty("trip", out var tripNode)
                || tripNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tripId = GetString(tripNode, "gtfsId");
            if (string.IsNullOrWhiteSpace(tripId))
                continue;

            var routeNode = tripNode.GetProperty("route");
            var routeId = GetString(routeNode, "gtfsId");
            if (string.IsNullOrWhiteSpace(routeId))
                routeId = UnknownRouteIdFallback;

            var shortName = GetString(routeNode, "shortName");
            if (string.IsNullOrWhiteSpace(shortName))
                shortName = routeId;

            var headsign = GetString(tripNode, "tripHeadsign");

            var groupKey = $"{routeId}|{headsign}";
            if (!grouped.TryGetValue(groupKey, out var group))
            {
                group = new DepartureGroupDto
                {
                    RouteId = routeId,
                    ShortName = shortName,
                    Headsign = headsign,
                    Departures = []
                };
                grouped[groupKey] = group;
            }

            var serviceDay = GetLong(item, "serviceDay");
            var scheduledDeparture = GetLong(item, "scheduledDeparture");
            var realtimeDeparture = GetNullableLong(item, "realtimeDeparture");
            var isRealtime = GetBool(item, "realtime");

            var scheduledUnix = serviceDay + scheduledDeparture;
            var scheduled = UnixToTime(scheduledUnix);

            string? estimated = null;
            int? delayMinutes = null;

            if (isRealtime && realtimeDeparture.HasValue)
            {
                var realtimeUnix = serviceDay + realtimeDeparture.Value;
                estimated = UnixToTime(realtimeUnix);
                delayMinutes = (int)Math.Round((realtimeDeparture.Value - scheduledDeparture) / 60.0);
            }

            group.Departures.Add(new DepartureDto
            {
                TripId = tripId,
                ScheduledTime = scheduled,
                EstimatedTime = estimated,
                DelayMinutes = delayMinutes,
                IsRealtime = isRealtime
            });
        }

        foreach (var group in grouped.Values)
        {
            group.Departures = group.Departures
                .OrderBy(d => ParseTimeMinutes(d.ScheduledTime))
                .ToList();
            result.Groups.Add(group);
        }

        result.Groups = result.Groups
            .OrderBy(g => g.ShortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Headsign, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
    }

    public async Task<bool> HealthCheckAsync(string instanceKey, CancellationToken ct = default)
    {
        const string query = "query Health { __typename }";
        var doc = await _otpClient.QueryAsync(instanceKey, query, ct: ct);
        return doc is not null;
    }

    private static bool TryGetDataNode(JsonDocument doc, string property, out JsonElement node)
    {
        node = default;
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!data.TryGetProperty(property, out node))
            return false;

        return true;
    }

    private static string GetString(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value))
            return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static bool GetBool(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value))
            return false;
        return value.ValueKind == JsonValueKind.True
               || (value.ValueKind == JsonValueKind.String
                   && bool.TryParse(value.GetString(), out var b)
                   && b);
    }

    private static long GetLong(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value))
            return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt64(),
            JsonValueKind.String when long.TryParse(value.GetString(), out var n) => n,
            _ => 0
        };
    }

    private static long? GetNullableLong(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.Number)
            return value.GetInt64();

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out var n))
        {
            return n;
        }

        return null;
    }

    private static double GetDouble(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value))
            return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) => n,
            _ => 0
        };
    }

    private static string UnixToTime(long unix)
        => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm");

    private static int ParseTimeMinutes(string hhmm)
    {
        var p = hhmm.Split(':');
        return p.Length >= 2
               && int.TryParse(p[0], out var h)
               && int.TryParse(p[1], out var m)
            ? h * 60 + m
            : int.MaxValue;
    }

    private static int MapModeToRouteType(string mode)
        => mode.ToUpperInvariant() switch
        {
            "TRAM" => 0,
            "SUBWAY" => 1,
            "RAIL" => 2,
            "BUS" => 3,
            "FERRY" => 4,
            "CABLE_CAR" => 5,
            "GONDOLA" => 6,
            "FUNICULAR" => 7,
            "TROLLEYBUS" => 11,
            _ => 3
        };

    private static int ResolveStopRouteType(JsonElement stopNode)
    {
        if (!stopNode.TryGetProperty("routes", out var routesNode)
            || routesNode.ValueKind != JsonValueKind.Array)
        {
            return 3;
        }

        var routeTypes = new List<int>();
        foreach (var route in routesNode.EnumerateArray())
        {
            var mode = GetString(route, "mode");
            if (string.IsNullOrWhiteSpace(mode))
                continue;
            routeTypes.Add(MapModeToRouteType(mode));
        }

        if (routeTypes.Count == 0)
            return 3;

        // Prefer more specific rail/tram icon categories over bus if mixed at a stop.
        if (routeTypes.Contains(0)) return 0;   // Tram
        if (routeTypes.Contains(2)) return 2;   // Rail
        if (routeTypes.Contains(1)) return 1;   // Subway
        if (routeTypes.Contains(11)) return 11; // Trolleybus
        if (routeTypes.Contains(4)) return 4;   // Ferry

        return routeTypes[0];
    }

    private static string? NormalizeColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().TrimStart('#');
    }
}
