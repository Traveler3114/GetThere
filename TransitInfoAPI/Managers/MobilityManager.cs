using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Managers;

public class MobilityManager
{
    private readonly TransitDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MobilityManager> _logger;
    private readonly PlaceMatchingManager _placeMatching;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _providerLocks = new();

    public MobilityManager(TransitDbContext db, IHttpClientFactory httpFactory, ILogger<MobilityManager> logger, PlaceMatchingManager placeMatching)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _placeMatching = placeMatching;
    }

    public async Task<List<MobilityStation>> GetStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.MobilityProvider)
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

    public async Task PollMobilityProviderAsync(int providerId, CancellationToken ct = default)
    {
        var sem = _providerLocks.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var provider = await _db.MobilityProviders
                .Include(mp => mp.Operator)
                .FirstOrDefaultAsync(mp => mp.Id == providerId && mp.IsActive, ct);

            if (provider is null) return;

            var http = _httpFactory.CreateClient();
            var url = provider.InternalUrl ?? provider.ExternalUrl;

            if (provider.FeedFormat == FeedFormat.NextbikeAPI)
            {
                await PollNextbikeAsync(provider, http, url, ct);
            }
            else if (provider.FeedFormat == FeedFormat.GBFS)
            {
                await PollGbfsAsync(provider, http, url, ct);
            }

            _logger.LogInformation("Polled mobility provider {ProviderId} successfully", providerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to poll mobility provider {ProviderId}", providerId);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task PollNextbikeAsync(MobilityProvider provider, HttpClient http, string url, CancellationToken ct)
    {
        var response = await http.GetStringAsync(url, ct);
        var root = JsonSerializer.Deserialize<NextbikeRoot>(response);
        if (root?.Countries is null) return;

        var existingByStationId = await _db.MobilityStations
            .Where(ms => ms.MobilityProviderId == provider.Id)
            .ToDictionaryAsync(ms => ms.StationId, ct);

        foreach (var country in root.Countries)
        {
            if (country.Cities is null) continue;

            foreach (var city in country.Cities)
            {
                if (city.Places is null) continue;

                foreach (var place in city.Places)
                {
                    if (existingByStationId.TryGetValue(place.Uid.ToString(), out var existing))
                    {
                        existing.AvailableVehicles = place.BikesAvailableToRent ?? place.Bikes;
                        existing.LastUpdated = DateTime.UtcNow;
                    }
                    else
                    {
                        _db.MobilityStations.Add(new MobilityStation
                        {
                            MobilityProviderId = provider.Id,
                            StationId = place.Uid.ToString(),
                            Name = place.Name ?? string.Empty,
                            Latitude = place.Lat,
                            Longitude = place.Lng,
                            Capacity = place.BikeRacks > 0 ? place.BikeRacks : null,
                            AvailableVehicles = place.BikesAvailableToRent ?? place.Bikes,
                            CountryId = await _placeMatching.DeriveCountryIdAsync(place.Lat, place.Lng, ct),
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task PollGbfsAsync(MobilityProvider provider, HttpClient http, string url, CancellationToken ct)
    {
        _logger.LogWarning("GBFS polling not implemented for provider {ProviderId}", provider.Id);
        await Task.CompletedTask;
    }

    public async Task<int> UpsertStationsFromCustomFeedAsync(int providerId, List<Dictionary<string, object?>> records, CancellationToken ct = default)
    {
        var existingByStationId = await _db.MobilityStations
            .Where(ms => ms.MobilityProviderId == providerId)
            .ToDictionaryAsync(ms => ms.StationId, ct);

        var countryCache = new Dictionary<string, int>();

        async Task<int> GetCountryId(double lat, double lng)
        {
            var key = $"{Math.Round(lat, 1)},{Math.Round(lng, 1)}";
            if (countryCache.TryGetValue(key, out var cached)) return cached;
            var id = await _placeMatching.DeriveCountryIdAsync(lat, lng, ct);
            countryCache[key] = id;
            return id;
        }

        int upserted = 0;
        foreach (var record in records)
        {
            var stationId = GetDictString(record, "stationId") ?? GetDictString(record, "station_id") ?? GetDictString(record, "uid");
            var name = GetDictString(record, "name");
            var lat = GetDictDouble(record, "latitude") ?? GetDictDouble(record, "lat");
            var lng = GetDictDouble(record, "longitude") ?? GetDictDouble(record, "lng") ?? GetDictDouble(record, "lon");

            if (stationId is null || name is null || lat is null || lng is null)
                continue;

            var capacity = GetDictInt(record, "capacity") ?? GetDictInt(record, "bike_racks");
            var availableVehicles = GetDictInt(record, "availableVehicles") ?? GetDictInt(record, "bikes_available_to_rent") ?? GetDictInt(record, "available_bikes") ?? 0;
            var countryName = GetDictString(record, "country_name");
            var countryCode = GetDictString(record, "country");
            var cityName = GetDictString(record, "city_name");

            if (existingByStationId.TryGetValue(stationId, out var existing))
            {
                existing.Name = name;
                existing.Latitude = lat.Value;
                existing.Longitude = lng.Value;
                existing.Capacity = capacity > 0 ? capacity : null;
                existing.AvailableVehicles = availableVehicles;
                existing.LastUpdated = DateTime.UtcNow;
                existing.CountryName = countryName;
                existing.CountryCode = countryCode;
                existing.CityName = cityName;
            }
            else
            {
                _db.MobilityStations.Add(new MobilityStation
                {
                    MobilityProviderId = providerId,
                    StationId = stationId,
                    Name = name,
                    Latitude = lat.Value,
                    Longitude = lng.Value,
                    Capacity = capacity > 0 ? capacity : null,
                    AvailableVehicles = availableVehicles,
                    CountryId = await GetCountryId(lat.Value, lng.Value),
                    LastUpdated = DateTime.UtcNow,
                    CountryName = countryName,
                    CountryCode = countryCode,
                    CityName = cityName
                });
            }

            upserted++;
        }

        await _db.SaveChangesAsync(ct);
        return upserted;
    }

    private static string? GetDictString(Dictionary<string, object?> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    private static double? GetDictDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is long l) return l;
        if (double.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int? GetDictInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    private class NextbikeRoot
    {
        public List<NextbikeCountry>? Countries { get; set; }
    }

    private class NextbikeCountry
    {
        public List<NextbikeCity>? Cities { get; set; }
    }

    private class NextbikeCity
    {
        public List<NextbikePlace>? Places { get; set; }
    }

    private class NextbikePlace
    {
        public int Uid { get; set; }
        public string? Name { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int Bikes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bikes_available_to_rent")]
        public int? BikesAvailableToRent { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bike_racks")]
        public int BikeRacks { get; set; }
    }
}
