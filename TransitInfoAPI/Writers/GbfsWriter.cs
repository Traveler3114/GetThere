using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace TransitInfoAPI.Writers;

public class GbfsWriter
{
    private readonly ILogger<GbfsWriter> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public GbfsWriter(ILogger<GbfsWriter> logger)
    {
        _logger = logger;
    }

    public Task<byte[]> ConvertAsync(List<Dictionary<string, object?>> records, int? mobilityProviderId, CancellationToken ct)
    {
        if (records.Count == 0) return Task.FromResult(Array.Empty<byte>());

        var first = records[0];
        bool isStations = first.ContainsKey("station_id");
        bool isFreeFloating = first.ContainsKey("bike_id")
            && first.ContainsKey("lat")
            && first.ContainsKey("lon");

        if (!isStations && !isFreeFloating)
        {
            _logger.LogWarning("GbfsWriter: records missing station_id or bike_id with lat/lon");
            return Task.FromResult(Array.Empty<byte>());
        }

        if (isStations)
        {
            var stations = new List<object>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var record in records)
            {
                var stationId = GetString(record, "station_id");
                var name = GetString(record, "name");
                var lat = GetDouble(record, "lat");
                var lng = GetDouble(record, "lon");

                if (stationId is null || name is null || lat is null || lng is null)
                    continue;

                stations.Add(new
                {
                    station_id = stationId,
                    name,
                    lat = lat.Value,
                    lon = lng.Value,
                    capacity = GetInt(record, "capacity"),
                    num_bikes_available = GetInt(record, "num_bikes_available") ?? 0,
                    num_docks_available = GetInt(record, "num_docks_available") ?? 0,
                    is_installed = 1,
                    is_renting = 1,
                    is_returning = 1,
                    last_reported = now
                });
            }

            var payload = new
            {
                stations
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            _logger.LogInformation("GbfsWriter: serialized {Count} station records", stations.Count);
            return Task.FromResult(Encoding.UTF8.GetBytes(json));
        }

        if (isFreeFloating)
        {
            var bikes = new List<object>();
            foreach (var record in records)
            {
                var bikeId = GetString(record, "bike_id");
                var lat = GetDouble(record, "lat");
                var lng = GetDouble(record, "lon");

                if (bikeId is null || lat is null || lng is null)
                    continue;

                bikes.Add(new
                {
                    bike_id = bikeId,
                    lat = lat.Value,
                    lon = lng.Value,
                    is_reserved = false,
                    is_disabled = false
                });
            }

            var payload = new
            {
                last_updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ttl = 0,
                data = new { bikes }
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            _logger.LogInformation("GbfsWriter: serialized {Count} free-floating vehicle records", bikes.Count);
            return Task.FromResult(Encoding.UTF8.GetBytes(json));
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    private static double? GetDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is long l) return l;
        if (double.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int? GetInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}
