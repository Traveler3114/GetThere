using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

public class PlaceMatchingOptions
{
    public int MaxDistanceMeters { get; set; } = 50000;
    public int CooldownHours { get; set; } = 0;
    public int DefaultCountryId { get; set; } = 1;
}

public class PlaceMatchingManager
{
    private static readonly Dictionary<string, string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Albania", ["AM"] = "Armenia", ["AT"] = "Austria", ["AZ"] = "Azerbaijan",
        ["BA"] = "Bosnia and Herzegovina", ["BE"] = "Belgium", ["BG"] = "Bulgaria",
        ["CH"] = "Switzerland", ["CZ"] = "Czech Republic", ["DE"] = "Germany",
        ["DK"] = "Denmark", ["EE"] = "Estonia", ["ES"] = "Spain", ["FI"] = "Finland",
        ["FR"] = "France", ["GB"] = "United Kingdom", ["GE"] = "Georgia",
        ["GR"] = "Greece", ["HR"] = "Croatia", ["HU"] = "Hungary", ["IE"] = "Ireland",
        ["IT"] = "Italy", ["LI"] = "Liechtenstein", ["LT"] = "Lithuania",
        ["LU"] = "Luxembourg", ["LV"] = "Latvia", ["MC"] = "Monaco",
        ["MD"] = "Moldova", ["ME"] = "Montenegro", ["NL"] = "Netherlands",
        ["NO"] = "Norway", ["PL"] = "Poland", ["PT"] = "Portugal", ["RO"] = "Romania",
        ["SE"] = "Sweden", ["SI"] = "Slovenia", ["SK"] = "Slovakia",
        ["SM"] = "San Marino", ["TR"] = "Turkey", ["UA"] = "Ukraine",
        ["VA"] = "Vatican City",
    };

    private readonly TransitDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaceMatchingManager> _logger;
    private readonly IOptions<PlaceMatchingOptions> _options;
    private readonly int _maxDistanceMeters;
    private readonly int _cooldownHours;
    private List<Place>? _placeCache;
    private Dictionary<string, List<Place>>? _placeGrid;
    private readonly Dictionary<string, int> _countryIdCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastMatchRun = DateTime.MinValue;
    private const double GridCellSizeDeg = 0.5;

    public PlaceMatchingManager(TransitDbContext db, IServiceScopeFactory scopeFactory, ILogger<PlaceMatchingManager> logger, IOptions<PlaceMatchingOptions> options)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
        _maxDistanceMeters = options.Value.MaxDistanceMeters;
        _cooldownHours = options.Value.CooldownHours;
    }

    public async Task LoadPlacesAsync(CancellationToken ct)
    {
        if (_placeCache is not null) return;
        _placeCache = await _db.Places.ToListAsync(ct);
        BuildPlaceGrid();
        _logger.LogInformation("Loaded {Count} places into cache ({CellCount} grid cells)", _placeCache.Count, _placeGrid?.Count);
    }

    private void BuildPlaceGrid()
    {
        _placeGrid = new Dictionary<string, List<Place>>();
        foreach (var place in _placeCache!)
        {
            var key = GetGridCellKey(place.Lat, place.Lon);
            if (!_placeGrid.TryGetValue(key, out var list))
                _placeGrid[key] = list = [];
            list.Add(place);
        }
    }

    private static string GetGridCellKey(double lat, double lon)
    {
        var cellLat = Math.Round(lat / GridCellSizeDeg) * GridCellSizeDeg;
        var cellLon = Math.Round(lon / GridCellSizeDeg) * GridCellSizeDeg;
        return $"{cellLat:F1}:{cellLon:F1}";
    }

    public Place? FindNearestPlace(double lat, double lon)
    {
        if (_placeCache is null || _placeCache.Count == 0) return null;
        if (_placeGrid is null) BuildPlaceGrid();

        Place? nearest = null;
        var minDist = double.MaxValue;

        var centerKey = GetGridCellKey(lat, lon);
        var parts = centerKey.Split(':');
        var centerLat = double.Parse(parts[0]);
        var centerLon = double.Parse(parts[1]);

        for (var dLat = -1; dLat <= 1; dLat++)
        {
            for (var dLon = -1; dLon <= 1; dLon++)
            {
                var key = $"{(centerLat + dLat * GridCellSizeDeg):F1}:{(centerLon + dLon * GridCellSizeDeg):F1}";
                if (_placeGrid!.TryGetValue(key, out var places))
                {
                    foreach (var place in places)
                    {
                        var dist = GeoUtils.CalculateDistanceMeters(lat, lon, place.Lat, place.Lon);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = place;
                        }
                    }
                }
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
                station.CountryId = await DeriveCountryIdAsync(station.Latitude, station.Longitude, ct);
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
            station.CountryId = await DeriveCountryIdAsync(station.Latitude, station.Longitude, ct);
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

    public async Task<int> DeriveCountryIdAsync(double lat, double lon, CancellationToken ct)
    {
        if (_placeCache is null)
            await LoadPlacesAsync(ct);
        var place = FindNearestPlace(lat, lon);
        if (place is not null && !string.IsNullOrEmpty(place.AdmCountryCode))
        {
            if (_countryIdCache.TryGetValue(place.AdmCountryCode, out var cachedId))
                return cachedId;
            var country = await _db.Countries.FirstOrDefaultAsync(c => c.IsoCode == place.AdmCountryCode, ct);
            if (country is not null)
            {
                _countryIdCache[place.AdmCountryCode] = country.Id;
                return country.Id;
            }
        }
        var iso = GeoCountryDetector.DetectCountryIso(lat, lon);
        if (iso is not null)
        {
            if (_countryIdCache.TryGetValue(iso, out var cachedId))
                return cachedId;
            var country = await _db.Countries.FirstOrDefaultAsync(c => c.IsoCode == iso, ct);
            if (country is not null)
            {
                _countryIdCache[iso] = country.Id;
                return country.Id;
            }
            var countryName = CountryNames.TryGetValue(iso, out var n) ? n : iso;
            country = new Country { IsoCode = iso, Name = countryName, Continent = "Unknown" };
            using (var scope = _scopeFactory.CreateScope())
            {
                var scopedDb = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
                scopedDb.Countries.Add(country);
                await scopedDb.SaveChangesAsync(ct);
            }
            _countryIdCache[iso] = country.Id;
            return country.Id;
        }
        return _options.Value.DefaultCountryId;
    }
}
