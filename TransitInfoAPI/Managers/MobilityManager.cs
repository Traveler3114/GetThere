using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class MobilityManager
{
    private readonly TransitDbContext _db;
    private readonly ILogger<MobilityManager> _logger;
    private readonly PlaceMatchingManager _placeMatching;

    public MobilityManager(TransitDbContext db, ILogger<MobilityManager> logger, PlaceMatchingManager placeMatching) { _db = db; _logger = logger; _placeMatching = placeMatching; }

    public async Task<List<MobilityStation>> GetStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.Operator)
            .Include(ms => ms.Country)
            .AsQueryable();

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / GeoConstants.KmPerDegree;
            var lonRange = radiusKm.Value / (GeoConstants.KmPerDegree * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<List<MobilityStationResponse>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId,
        int page, int perPage, CancellationToken ct)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.Operator)
            .Include(ms => ms.Country)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(ms => ms.CountryId == countryId.Value);

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / GeoConstants.KmPerDegree;
            var lonRange = radiusKm.Value / (GeoConstants.KmPerDegree * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        return await query
            .OrderBy(ms => ms.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(MobilityStationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, CancellationToken ct)
    {
        var query = _db.MobilityStations.AsQueryable();

        if (countryId.HasValue)
            query = query.Where(ms => ms.CountryId == countryId.Value);

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / GeoConstants.KmPerDegree;
            var lonRange = radiusKm.Value / (GeoConstants.KmPerDegree * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        return await query.CountAsync(ct);
    }

    public async Task<object> GetAllGeoJsonAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int limit, CancellationToken ct)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.Country)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(ms => ms.CountryId == countryId.Value);

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / GeoConstants.KmPerDegree;
            var lonRange = radiusKm.Value / (GeoConstants.KmPerDegree * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        var stations = await query.OrderBy(ms => ms.Id).Take(limit)
            .Select(MobilityStationMapper.ToResponseExpression)
            .ToListAsync(ct);

        return GeoJsonGeometry.ToPointCollection(stations,
            s => s.Latitude, s => s.Longitude,
            s => new Dictionary<string, object?>
            {
                ["id"] = s.Id,
                ["stationId"] = s.StationId,
                ["name"] = s.Name,
                ["providerName"] = s.ProviderName,
                ["capacity"] = s.Capacity,
                ["availableVehicles"] = s.AvailableVehicles,
                ["countryName"] = s.CountryName
            });
    }

    public async Task<List<string>> GetCountriesAsync(CancellationToken ct)
    {
        var names = await _db.MobilityStations
            .Where(ms => ms.Country != null)
            .Select(ms => ms.Country.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
        return names;
    }

    public async Task<int> UpsertStationsFromGbfsBytesAsync(int operatorId, byte[] gbfsData, CancellationToken ct = default)
    {
        var doc = JsonDocument.Parse(gbfsData);
        var root = doc.RootElement;

        if (!root.TryGetProperty("stations", out var stationsElement))
            return 0;

        // Grouped rather than ToDictionary: a duplicate station_id for one operator threw and killed
        // the whole poll, and nothing prevents duplicates — there is no unique index on
        // (OperatorId, StationId). Last row wins, which matches the upsert semantics below.
        var existingByStationId = (await _db.MobilityStations
                .Where(ms => ms.OperatorId == operatorId)
                .ToListAsync(ct))
            .GroupBy(ms => ms.StationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        int upserted = 0;
        int skipped = 0;
        foreach (var station in stationsElement.EnumerateArray())
        {
            // Every field is read defensively. station_id and name already were; lat, lon and the
            // counts were not, so a station missing "lat" threw KeyNotFoundException and a "capacity"
            // that was null or a string threw InvalidOperationException — both legal GBFS, and either
            // one aborted the upsert for the entire feed.
            var stationId = station.TryGetProperty("station_id", out var idEl) ? idEl.GetString() : null;
            var name = station.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var lat = ReadDouble(station, "lat");
            var lon = ReadDouble(station, "lon");

            if (string.IsNullOrWhiteSpace(stationId) || string.IsNullOrWhiteSpace(name) || lat is null || lon is null)
            {
                skipped++;
                continue;
            }

            // Same range check ParseStops applies to GTFS stops. A (0,0) dock is a missing coordinate,
            // not a dock in the Gulf of Guinea.
            if (lat is < -90 or > 90 || lon is < -180 or > 180 || (lat == 0.0 && lon == 0.0))
            {
                _logger.LogWarning("Skipping mobility station {StationId} with invalid coordinates ({Lat}, {Lon})", stationId, lat, lon);
                skipped++;
                continue;
            }

            var capacity = ReadInt(station, "capacity") ?? 0;
            var numBikes = ReadInt(station, "num_bikes_available") ?? 0;

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

        if (skipped > 0)
            _logger.LogWarning("Skipped {Skipped} GBFS station(s) for operator {OperatorId} with missing or invalid fields", skipped, operatorId);

        _logger.LogInformation("Upserted {Count} stations from GBFS data for operator {OperatorId}", upserted, operatorId);
        return upserted;
    }

    /// <summary>Reads a JSON number that a provider may have encoded as a string, or omitted.</summary>
    private static double? ReadDouble(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return null;

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    /// <summary>As <see cref="ReadDouble"/>. GBFS permits null counts, and some providers send strings.</summary>
    private static int? ReadInt(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return null;

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            // A provider reporting 12.0 for a capacity is still telling us 12.
            JsonValueKind.Number when el.TryGetDouble(out var d) => (int)d,
            JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    public async Task<int> UpsertStationsFromRecordsAsync(int operatorId, List<Dictionary<string, object?>> records, CancellationToken ct = default)
    {
        if (records.Count == 0) return 0;

        // Grouped for the same reason as the GBFS path: a duplicate station id must not throw.
        var existingByStationId = (await _db.MobilityStations
                .Where(ms => ms.OperatorId == operatorId)
                .ToListAsync(ct))
            .GroupBy(ms => ms.StationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

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
