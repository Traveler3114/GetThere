using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

public class PlaceMatchingManager
{
    private readonly TransitDbContext _db;
    private readonly ILogger<PlaceMatchingManager> _logger;
    private List<Place>? _placeCache;

    public PlaceMatchingManager(TransitDbContext db, ILogger<PlaceMatchingManager> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LoadPlacesAsync(CancellationToken ct)
    {
        _placeCache = await _db.Places
            .OrderByDescending(p => p.Population)
            .ToListAsync(ct);
        _logger.LogInformation("Loaded {Count} places into cache", _placeCache.Count);
    }

    public Place? FindNearestPlace(double lat, double lon)
    {
        if (_placeCache is null || _placeCache.Count == 0) return null;

        Place? nearest = null;
        var minDist = double.MaxValue;

        foreach (var place in _placeCache)
        {
            var dist = CalculateDistanceMeters(lat, lon, place.Lat, place.Lon);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = place;
            }
        }

        return minDist < 50_000 ? nearest : null;
    }

    public async Task MatchStationsToPlacesAsync(CancellationToken ct)
    {
        var stations = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == null)
            .ToListAsync(ct);

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

    public async Task MatchOperatorsToPlacesAsync(CancellationToken ct)
    {
        // TODO: implement when Operator gets PlaceId
        await Task.CompletedTask;
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}
