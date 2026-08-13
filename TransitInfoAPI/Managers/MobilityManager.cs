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

    /// <summary>
    /// The one definition of what filters a mobility-station list, composed by the list, the count
    /// and the GeoJSON read below.
    /// <para>
    /// Same reasoning as <see cref="StationManager"/>'s: these were three copies of the same
    /// bounding-box arithmetic, and <c>countryName</c> was accepted by the controller and passed to
    /// none of them — the admin console's country filter on the mobility page built the query string
    /// and the server ignored it.
    /// </para>
    /// </summary>
    private IQueryable<MobilityStation> BuildQuery(
        double? lat, double? lon, double? radiusKm, int? countryId, string? countryName)
    {
        var query = _db.MobilityStations.AsQueryable();

        if (countryId.HasValue)
            query = query.Where(ms => ms.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(ms =>
                _db.Countries.Any(c => c.Name == countryName && c.Id == ms.CountryId));

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / GeoConstants.KmPerDegree;

            // Clamped before the cosine: at the poles it reaches zero and the division yields
            // Infinity, which makes the longitude bounds meaningless.
            var clampedLat = Math.Clamp(lat.Value, -89.9, 89.9);
            var lonRange = radiusKm.Value / (GeoConstants.KmPerDegree * Math.Cos(clampedLat * Math.PI / 180));

            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        return query;
    }

    /// <summary>
    /// Every mobility station in a radius, entities rather than responses.
    /// <para>
    /// <b>Nothing calls this.</b> <c>MobilityController</c> goes through
    /// <see cref="GetAllAsync"/>, <see cref="GetTotalCountAsync"/> and
    /// <see cref="GetAllGeoJsonAsync"/>; the polling worker goes through
    /// <see cref="UpsertStationsFromGbfsBytesAsync"/>. It is left in place rather than deleted
    /// because that is a call for whoever owns the roadmap, but it is worth knowing that it is
    /// unreferenced before trusting anything it does.
    /// </para>
    /// <para>
    /// It was also the fourth copy of the bounding-box arithmetic — <see cref="BuildQuery"/>'s note
    /// says three, which was the count among the <em>reachable</em> paths — and the one copy that
    /// never got the latitude clamp, so it still divided by <c>Math.Cos</c> at the pole and built
    /// longitude bounds out of Infinity. Routing it through <see cref="BuildQuery"/> fixes that and
    /// stops the next change to the filter having to remember a call site nothing exercises.
    /// </para>
    /// <para>
    /// Still uncapped: with no coordinates it returns every mobility station there is. The three
    /// reachable reads all bound their result (a page, or the GeoJSON ceiling of 5,000); this one
    /// has no caller to say what its bound should be.
    /// </para>
    /// </summary>
    public async Task<List<MobilityStation>> GetStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        return await BuildQuery(lat, lon, radiusKm, countryId: null, countryName: null)
            .Include(ms => ms.Operator)
            .Include(ms => ms.Country)
            .ToListAsync(ct);
    }

    public async Task<List<MobilityStationResponse>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, string? countryName,
        int page, int perPage, CancellationToken ct)
    {
        // No Include: the query projects through MobilityStationMapper.ToResponseExpression, which
        // pulls the operator and country columns it needs itself.
        return await BuildQuery(lat, lon, radiusKm, countryId, countryName)
            .OrderBy(ms => ms.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(MobilityStationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, string? countryName, CancellationToken ct)
    {
        return await BuildQuery(lat, lon, radiusKm, countryId, countryName).CountAsync(ct);
    }

    public async Task<object> GetAllGeoJsonAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, string? countryName, int limit, CancellationToken ct)
    {
        var stations = await BuildQuery(lat, lon, radiusKm, countryId, countryName)
            .OrderBy(ms => ms.Id).Take(limit)
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
        // Disposed: JsonDocument rents its backing buffer from the array pool, and this runs on
        // every poll of every GBFS operator. Leaking it leaks pooled memory on a schedule.
        using var doc = JsonDocument.Parse(gbfsData);
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

            // The shared predicate, which is also what ParseStops applies to GTFS stops. A (0,0)
            // dock is a missing coordinate, not a dock in the Gulf of Guinea.
            if (!GeoBounds.IsUsable(lat.Value, lon.Value))
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

    /// <summary>
    /// Upserts docks from extracted rows rather than from a GBFS document — the shape a custom
    /// source produces.
    /// <para>
    /// <b>Nothing calls this either.</b> The custom-source path it was evidently written for stops
    /// at <c>TransitSection</c>, which has no mobility section, so extracted rows never reach it.
    /// That is why its two divergences from the GBFS path above went unnoticed: it parsed
    /// coordinates under the server's culture, and it applied no range check at all before writing
    /// them. Both are fixed here so that wiring it up later does not import the bugs with it.
    /// </para>
    /// </summary>
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

            // The same range check the GBFS path applies. This path had none at all, so a record
            // whose coordinate had been misread — see GetDouble, which parsed under the server's
            // culture until this commit — was written to the database unchallenged.
            if (!GeoBounds.IsUsable(lat.Value, lon.Value))
            {
                _logger.LogWarning("Skipping mobility station {StationId} with invalid coordinates ({Lat}, {Lon})", stationId, lat, lon);
                continue;
            }

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

    /// <summary>
    /// Reads a coordinate out of an extracted row.
    /// <para>
    /// Invariant culture, like <see cref="ReadDouble"/> on the GBFS path. The bare
    /// <c>double.TryParse(s, out _)</c> this used parses under the <em>server's</em> culture, whose
    /// default styles include <c>AllowThousands</c> — so in any culture that groups with a dot,
    /// <c>"45.81"</c> reads as <c>4581</c>. Not a parse failure that shows up in a log: a silently
    /// wrong number, from a value that came out of an operator's file rather than out of code.
    /// </para>
    /// <para>
    /// <c>CA1305</c>, the analyzer for exactly this, is turned off in <c>.editorconfig</c>, which is
    /// why nothing flagged it.
    /// </para>
    /// </summary>
    private static double? GetDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is long l) return l;
        if (v is decimal m) return (double)m;
        return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>As <see cref="GetDouble"/>, and invariant for the same reason.</summary>
    private static int? GetInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
