using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

public class PlaceMatchingOptions
{
    public int MaxDistanceMeters { get; set; } = 50000;
    public int CooldownHours { get; set; }

    /// <summary>ISO 3166-1 alpha-2 code of the country to attribute a location to when detection fails.</summary>
    public string DefaultCountryIsoCode { get; set; } = "HR";
}

public class PlaceMatchingManager
{
    private static readonly Dictionary<string, string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Albania",
        ["AM"] = "Armenia",
        ["AT"] = "Austria",
        ["AZ"] = "Azerbaijan",
        ["BA"] = "Bosnia and Herzegovina",
        ["BE"] = "Belgium",
        ["BG"] = "Bulgaria",
        ["CH"] = "Switzerland",
        ["CZ"] = "Czech Republic",
        ["DE"] = "Germany",
        ["DK"] = "Denmark",
        ["EE"] = "Estonia",
        ["ES"] = "Spain",
        ["FI"] = "Finland",
        ["FR"] = "France",
        ["GB"] = "United Kingdom",
        ["GE"] = "Georgia",
        ["GR"] = "Greece",
        ["HR"] = "Croatia",
        ["HU"] = "Hungary",
        ["IE"] = "Ireland",
        ["IT"] = "Italy",
        ["LI"] = "Liechtenstein",
        ["LT"] = "Lithuania",
        ["LU"] = "Luxembourg",
        ["LV"] = "Latvia",
        ["MC"] = "Monaco",
        ["MD"] = "Moldova",
        ["ME"] = "Montenegro",
        ["NL"] = "Netherlands",
        ["NO"] = "Norway",
        ["PL"] = "Poland",
        ["PT"] = "Portugal",
        ["RO"] = "Romania",
        ["SE"] = "Sweden",
        ["SI"] = "Slovenia",
        ["SK"] = "Slovakia",
        ["SM"] = "San Marino",
        ["TR"] = "Turkey",
        ["UA"] = "Ukraine",
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
    /// <summary>
    /// When the last full match ran, shared across scopes.
    /// <para>
    /// This was an instance field on a service registered as <b>scoped</b>, so every request and
    /// every import got a fresh <c>DateTime.MinValue</c> and the cooldown below could never be true —
    /// <c>PlaceMatching:CooldownHours</c> was dead configuration and the full match ran after every
    /// import. Static so the window actually spans instances; interlocked because feed imports run
    /// three at a time.
    /// </para>
    /// </summary>
    private static long _lastMatchRunTicks;
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

        // AsNoTracking: the cache is read-only (callers copy Id/AdmCountryCode/AdmRegionCode off it
        // and never mutate a Place), and tracking the whole table put every row into the change
        // tracker for the lifetime of the scope — which, during a feed import, is the whole import.
        _placeCache = await _db.Places.AsNoTracking().ToListAsync(ct);
        BuildPlaceGrid();
        _logger.LogInformation("Loaded {Count} places into cache ({CellCount} grid cells)", _placeCache.Count, _placeGrid?.Count);
    }

    private void BuildPlaceGrid()
    {
        _placeGrid = [];
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

        var centerLat = Math.Round(lat / GridCellSizeDeg) * GridCellSizeDeg;
        var centerLon = Math.Round(lon / GridCellSizeDeg) * GridCellSizeDeg;

        // The window has to cover MaxDistanceMeters in both axes. A fixed 3x3 spans one cell either
        // side — 0.5 degrees, about 55 km of latitude but only ~39 km of longitude at Croatian
        // latitudes and less further north — so a place 40-50 km due east fell outside the cells
        // searched and was never considered. Widened by however many cells the threshold needs.
        var latCells = (int)Math.Ceiling(_maxDistanceMeters / 111_320.0 / GridCellSizeDeg);
        var lonMetresPerDegree = Math.Max(111_320.0 * Math.Cos(lat * Math.PI / 180), 1);
        var lonCells = (int)Math.Ceiling(_maxDistanceMeters / lonMetresPerDegree / GridCellSizeDeg);

        for (var dLat = -latCells; dLat <= latCells; dLat++)
        {
            for (var dLon = -lonCells; dLon <= lonCells; dLon++)
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

    /// <summary>
    /// Attaches every unmatched station, and every station whose place has drifted, to its nearest
    /// place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This one relies on its caller to have loaded the place cache; the other two entry points
    /// do not.</b> <see cref="RematchStationAsync"/> and <see cref="DeriveCountryIdAsync"/> both call
    /// <see cref="LoadPlacesAsync"/> defensively when <c>_placeCache</c> is null. This does not — and
    /// <see cref="FindNearestPlace"/> returns null rather than throwing when the cache is empty, so
    /// calling this without loading first matches nothing at all and logs "Matched 0 stations to
    /// places", which reads as "nothing needed doing" rather than "I was never initialised".
    /// <c>FeedManager</c> does call <c>LoadPlacesAsync</c> immediately before this, so the live path
    /// is correct; it is the next caller that the asymmetry catches.
    /// </para>
    /// <para>
    /// <b><see cref="FindNearestPlace"/> runs twice per matched station.</b> Once in the loop below,
    /// then again inside <see cref="DeriveCountryIdAsync"/> with the same coordinates — which
    /// re-scans the grid neighbourhood and re-computes the great-circle distance to every place in
    /// it, to reach a place the caller is already holding. Passing the resolved place in would halve
    /// the work; it needs <c>DeriveCountryIdAsync</c>'s signature changed, and that method is public
    /// and also called from <see cref="RematchStationAsync"/>.
    /// </para>
    /// <para>
    /// The cooldown is also consumed before the work runs, not after: if this throws partway, the
    /// stamp has already moved and the next attempt is skipped for the full window despite nothing
    /// having been matched.
    /// </para>
    /// </remarks>
    public async Task MatchStationsToPlacesAsync(CancellationToken ct)
    {
        if (_cooldownHours > 0)
        {
            var last = new DateTime(Interlocked.Read(ref _lastMatchRunTicks), DateTimeKind.Utc);
            if ((DateTime.UtcNow - last).TotalHours < _cooldownHours)
            {
                _logger.LogDebug("Skipping place matching — last run was less than {Cooldown}h ago", _cooldownHours);
                return;
            }
        }
        Interlocked.Exchange(ref _lastMatchRunTicks, DateTime.UtcNow.Ticks);

        var stations = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == null)
            .ToListAsync(ct);

        // Narrowed in SQL before the exact test runs in C#. This loaded *every* station that had a
        // place, each with its Place, and then filtered the whole set in memory — a full-table read
        // after every import, with T8 removing the only brake on how often that happened.
        //
        // The bounding box is a cheap superset of "further than 500 m": anything outside it is
        // certainly beyond the threshold, so only the handful inside it needs the precise
        // great-circle check below.
        const double StalePlaceDistanceMeters = 500;
        var degreeBuffer = StalePlaceDistanceMeters / 111_320.0;

        var candidates = await _db.CanonicalStations
            .Where(cs => cs.PlaceId != null && cs.Place != null)
            .Where(cs => cs.Latitude > cs.Place!.Lat + degreeBuffer
                      || cs.Latitude < cs.Place!.Lat - degreeBuffer
                      || cs.Longitude > cs.Place!.Lon + degreeBuffer
                      || cs.Longitude < cs.Place!.Lon - degreeBuffer)
            .ToListAsync(ct);

        var stale = candidates
            .Where(cs => GeoUtils.CalculateDistanceMeters(
                cs.Latitude, cs.Longitude, cs.Place!.Lat, cs.Place!.Lon) > StalePlaceDistanceMeters)
            .ToList();
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
        _logger.LogInformation("Re-matched station {StationId} to place {PlaceId}", stationId, station.PlaceId?.ToString(CultureInfo.InvariantCulture) ?? "null");
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
                try
                {
                    scopedDb.Countries.Add(country);
                    await scopedDb.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Another import created it first — the poller runs three feeds in parallel and
                    // they routinely meet the same new country. The row we wanted now exists, so read
                    // it back rather than surfacing the constraint as a 500.
                    scopedDb.Entry(country).State = EntityState.Detached;
                    country = await scopedDb.Countries.FirstAsync(c => c.IsoCode == iso, ct);
                }
            }
            _countryIdCache[iso] = country.Id;
            return country.Id;
        }

        return await ResolveFallbackCountryIdAsync(ct);
    }

    /// <summary>
    /// Country used when the location cannot be attributed to one.
    /// <para>
    /// Configured by ISO code rather than by primary key: <c>DefaultCountryId</c> was a raw identity
    /// value that defaulted to <c>1</c> in code while configuration set <c>2</c>, so the fallback
    /// silently pointed at a different country depending on which one applied — and at whatever
    /// country happened to occupy that row.
    /// </para>
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private async Task<int> ResolveFallbackCountryIdAsync(CancellationToken ct)
    {
        var iso = _options.Value.DefaultCountryIsoCode;

        if (_countryIdCache.TryGetValue(iso, out var cachedId))
            return cachedId;

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.IsoCode == iso, ct)
            ?? throw new Exceptions.AppException(
                $"Fallback country '{iso}' (PlaceMatching:DefaultCountryIsoCode) does not exist.",
                500, "FALLBACK_COUNTRY_MISSING");

        _countryIdCache[iso] = country.Id;
        return country.Id;
    }
}
