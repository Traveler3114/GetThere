using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

public class MobilityManager
{
    private readonly TransitDbContext _db;
    private readonly ILogger<MobilityManager> _logger;
    private readonly PlaceMatchingManager _placeMatching;

    public MobilityManager(TransitDbContext db, ILogger<MobilityManager> logger, PlaceMatchingManager placeMatching)
    {
        _db = db;
        _logger = logger;
        _placeMatching = placeMatching;
    }

    public async Task<List<MobilityStation>> GetStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.Operator)
            .Include(ms => ms.Country)
            .AsQueryable();

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / 111.0;
            var lonRange = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<int> UpsertStationsFromGbfsBytesAsync(int operatorId, byte[] gbfsData, CancellationToken ct = default)
    {
        var doc = JsonDocument.Parse(gbfsData);
        var root = doc.RootElement;

        if (!root.TryGetProperty("stations", out var stationsElement))
            return 0;

        var existingByStationId = await _db.MobilityStations
            .Where(ms => ms.OperatorId == operatorId)
            .ToDictionaryAsync(ms => ms.StationId, ct);

        int upserted = 0;
        foreach (var station in stationsElement.EnumerateArray())
        {
            var stationId = station.GetProperty("station_id").GetString();
            var name = station.GetProperty("name").GetString();
            if (stationId is null || name is null) continue;

            var lat = station.GetProperty("lat").GetDouble();
            var lon = station.GetProperty("lon").GetDouble();
            var capacity = station.TryGetProperty("capacity", out var cap) ? cap.GetInt32() : 0;
            var numBikes = station.TryGetProperty("num_bikes_available", out var bikes) ? bikes.GetInt32() : 0;

            if (existingByStationId.TryGetValue(stationId, out var existing))
            {
                existing.Name = name;
                existing.Latitude = lat;
                existing.Longitude = lon;
                existing.Capacity = capacity > 0 ? capacity : null;
                existing.AvailableVehicles = numBikes;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                _db.MobilityStations.Add(new MobilityStation
                {
                    OperatorId = operatorId,
                    StationId = stationId,
                    Name = name,
                    Latitude = lat,
                    Longitude = lon,
                    Capacity = capacity > 0 ? capacity : null,
                    AvailableVehicles = numBikes,
                    CountryId = await _placeMatching.DeriveCountryIdAsync(lat, lon, ct),
                    LastUpdated = DateTime.UtcNow
                });
            }

            upserted++;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Upserted {Count} stations from GBFS data for operator {OperatorId}", upserted, operatorId);
        return upserted;
    }

    public async Task<int> UpsertStationsFromRecordsAsync(int operatorId, List<Dictionary<string, object?>> records, CancellationToken ct = default)
    {
        if (records.Count == 0) return 0;

        var existingByStationId = await _db.MobilityStations
            .Where(ms => ms.OperatorId == operatorId)
            .ToDictionaryAsync(ms => ms.StationId, ct);

        int upserted = 0;
        foreach (var record in records)
        {
            var stationId = GetString(record, "station_id");
            var name = GetString(record, "name");
            var lat = GetDouble(record, "lat");
            var lon = GetDouble(record, "lon");

            if (stationId is null || name is null || lat is null || lon is null)
                continue;

            var capacity = GetInt(record, "capacity");
            var numBikes = GetInt(record, "num_bikes_available") ?? 0;

            if (existingByStationId.TryGetValue(stationId, out var existing))
            {
                existing.Name = name;
                existing.Latitude = lat.Value;
                existing.Longitude = lon.Value;
                existing.Capacity = capacity > 0 ? capacity : null;
                existing.AvailableVehicles = numBikes;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                _db.MobilityStations.Add(new MobilityStation
                {
                    OperatorId = operatorId,
                    StationId = stationId,
                    Name = name,
                    Latitude = lat.Value,
                    Longitude = lon.Value,
                    Capacity = capacity > 0 ? capacity : null,
                    AvailableVehicles = numBikes,
                    CountryId = await _placeMatching.DeriveCountryIdAsync(lat.Value, lon.Value, ct),
                    LastUpdated = DateTime.UtcNow
                });
            }

            upserted++;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Upserted {Count} stations from records for operator {OperatorId}", upserted, operatorId);
        return upserted;
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
        if (v is decimal m) return (double)m;
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
