using Google.Protobuf;
using Microsoft.Extensions.Logging;

using TransitRealtime;

namespace TransitInfoAPI.Writers;

public class GtfsRealtimeWriter
{
    private readonly ILogger<GtfsRealtimeWriter> _logger;

    public GtfsRealtimeWriter(ILogger<GtfsRealtimeWriter> logger)
    {
        _logger = logger;
    }

    public Task<byte[]> ConvertAsync(List<Dictionary<string, object?>> records, CancellationToken ct)
    {
        if (records.Count == 0) return Task.FromResult(Array.Empty<byte>());

        var first = records[0];
        bool isVehicles = first.ContainsKey("vehicleId") || first.ContainsKey("vehicle_id");
        bool isTripUpdates = first.ContainsKey("tripId") || first.ContainsKey("trip_id");

        if (!isVehicles && !isTripUpdates)
        {
            _logger.LogWarning("GtfsRealtime records missing required fields");
            return Task.FromResult(Array.Empty<byte>());
        }

        var feedMessage = new FeedMessage();
        var header = new FeedHeader
        {
            GtfsRealtimeVersion = "2.0",
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        feedMessage.Header = header;

        if (isVehicles)
        {
            int count = 0;
            foreach (var record in records)
            {
                var vehicleId = GetString(record, "vehicleId") ?? GetString(record, "vehicle_id");
                var lat = GetDouble(record, "latitude") ?? GetDouble(record, "lat");
                var lng = GetDouble(record, "longitude") ?? GetDouble(record, "lng") ?? GetDouble(record, "lon");
                var routeId = GetString(record, "routeId") ?? GetString(record, "route_id");
                var tripId = GetString(record, "tripId") ?? GetString(record, "trip_id");
                var bearing = GetDouble(record, "bearing");

                if (vehicleId is null || lat is null || lng is null)
                    continue;

                var entity = new FeedEntity
                {
                    Id = vehicleId,
                    Vehicle = new VehiclePosition
                    {
                        Trip = new TripDescriptor(),
                        Position = new Position
                        {
                            Latitude = (float)lat.Value,
                            Longitude = (float)lng.Value
                        },
                        Vehicle = new VehicleDescriptor { Id = vehicleId }
                    }
                };

                if (!string.IsNullOrEmpty(routeId))
                    entity.Vehicle.Trip.RouteId = routeId;
                if (!string.IsNullOrEmpty(tripId))
                    entity.Vehicle.Trip.TripId = tripId;
                if (bearing.HasValue)
                    entity.Vehicle.Position.Bearing = (float)bearing.Value;

                entity.Vehicle.Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                feedMessage.Entity.Add(entity);
                count++;
            }

            _logger.LogInformation("GtfsRealtimeWriter: serialized {Count} vehicle positions", count);
        }

        if (isTripUpdates)
        {
            int count = 0;
            foreach (var record in records)
            {
                var tripId = GetString(record, "tripId") ?? GetString(record, "trip_id");
                var stopSequence = GetInt(record, "stopSequence") ?? GetInt(record, "stop_sequence");
                var delaySeconds = GetInt(record, "delaySeconds") ?? GetInt(record, "delay_seconds");

                if (tripId is null || stopSequence is null)
                    continue;

                var entity = new FeedEntity
                {
                    Id = $"{tripId}-{stopSequence}",
                    TripUpdate = new TripUpdate
                    {
                        Trip = new TripDescriptor { TripId = tripId },
                        StopTimeUpdate =
                        {
                            new TripUpdate.Types.StopTimeUpdate
                            {
                                StopSequence = (uint)stopSequence.Value,
                                Departure = new TripUpdate.Types.StopTimeEvent { Delay = delaySeconds ?? 0 }
                            }
                        }
                    }
                };

                feedMessage.Entity.Add(entity);
                count++;
            }

            _logger.LogInformation("GtfsRealtimeWriter: serialized {Count} trip updates", count);
        }

        using var ms = new MemoryStream();
        feedMessage.WriteTo(ms);
        return Task.FromResult(ms.ToArray());
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
