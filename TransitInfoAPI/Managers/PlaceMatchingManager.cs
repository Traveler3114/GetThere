using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

public class PlaceMatchingOptions
{
    public int MaxDistanceMeters { get; set; } = 50000;
    public int CooldownHours { get; set; } = 0;
}

public class PlaceMatchingManager
{
    private readonly TransitDbContext _db;
    private readonly ILogger<PlaceMatchingManager> _logger;
    private readonly int _maxDistanceMeters;
    private readonly int _cooldownHours;
    private List<Place>? _placeCache;
    private DateTime _lastMatchRun = DateTime.MinValue;

    public PlaceMatchingManager(TransitDbContext db, ILogger<PlaceMatchingManager> logger, IOptions<PlaceMatchingOptions> options)
    {
        _db = db;
        _logger = logger;
        _maxDistanceMeters = options.Value.MaxDistanceMeters;
        _cooldownHours = options.Value.CooldownHours;
    }

    public async Task LoadPlacesAsync(CancellationToken ct)
    {
        if (_placeCache is not null) return;
        _placeCache = await _db.Places.ToListAsync(ct);
        _logger.LogInformation("Loaded {Count} places into cache", _placeCache.Count);
    }

    // O(n) scan — acceptable for ~500 places. Monitor if dataset grows 10×.
    public Place? FindNearestPlace(double lat, double lon)
    {
        if (_placeCache is null || _placeCache.Count == 0) return null;

        Place? nearest = null;
        var minDist = double.MaxValue;

        foreach (var place in _placeCache)
        {
            var dist = GeoUtils.CalculateDistanceMeters(lat, lon, place.Lat, place.Lon);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = place;
            }
        }

        return minDist < _maxDistanceMeters ? nearest : null;
    }

    public async Task MatchStationsToPlacesAsync(CancellationToken ct)
    {
        if (_cooldownHours > 0 && (DateTime.UtcNow - _lastMatchRun).TotalHours < _cooldownHours)
        {
            _logger.LogDebug("Skipping place matching — last run was less than {Cooldown}h ago", _cooldownHours);
            return;
        }
        _lastMatchRun = DateTime.UtcNow;

        var stations = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == null)
            .ToListAsync(ct);

        var stale = await _db.CanonicalStations
            .Include(cs => cs.Place)
            .Where(cs => cs.PlaceId != null && cs.Place != null)
            .ToListAsync(ct);
        stale = stale.Where(cs =>
            GeoUtils.CalculateDistanceMeters(
                cs.Latitude, cs.Longitude, cs.Place!.Lat, cs.Place!.Lon) > 500).ToList();
        stations.AddRange(stale);

        var matched = 0;
        foreach (var station in stations)
        {
            var place = FindNearestPlace(station.Latitude, station.Longitude);
            if (place is not null)
            {
                station.PlaceId = place.Id;
                station.AdmCountryCode = place.AdmCountryCode;
                station.AdmRegionCode = place.AdmRegionCode;
                matched++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Matched {Count} stations to places", matched);
    }

    public async Task RematchStationAsync(int stationId, CancellationToken ct)
    {
        if (_placeCache is null || _placeCache.Count == 0)
            await LoadPlacesAsync(ct);

        var station = await _db.CanonicalStations.FindAsync([stationId], ct);
        if (station is null) return;

        var place = FindNearestPlace(station.Latitude, station.Longitude);
        if (place is not null)
        {
            station.PlaceId = place.Id;
            station.AdmCountryCode = place.AdmCountryCode;
            station.AdmRegionCode = place.AdmRegionCode;
        }
        else
        {
            station.PlaceId = null;
            station.AdmCountryCode = null;
            station.AdmRegionCode = null;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Re-matched station {StationId} to place {PlaceId}", stationId, station.PlaceId?.ToString() ?? "null");
    }

}
