using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Core;
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
    private readonly RealtimeManager _realtime;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _providerLocks = new();

    public MobilityManager(TransitDbContext db, IHttpClientFactory httpFactory, ILogger<MobilityManager> logger, PlaceMatchingManager placeMatching, RealtimeManager realtime)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _placeMatching = placeMatching;
        _realtime = realtime;
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
            var url = provider.Url;

            if (string.IsNullOrWhiteSpace(url)) return;

            if (provider.FeedFormat == FeedFormat.GBFS)
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

    private async Task PollGbfsAsync(MobilityProvider provider, HttpClient http, string url, CancellationToken ct)
    {
        var json = await http.GetStringAsync(url, ct);
        var discovery = JsonSerializer.Deserialize<GbfsRoot>(json);
        var feeds = discovery?.Data?.GetFeeds()?.Feeds;
        if (feeds is null || feeds.Count == 0)
        {
            _logger.LogWarning("GBFS discovery returned no feeds for provider {ProviderId}", provider.Id);
            return;
        }

        var stationInfoUrl = feeds.FirstOrDefault(f => f.Name == "station_information")?.Url;
        var stationStatusUrl = feeds.FirstOrDefault(f => f.Name == "station_status")?.Url;
        var freeBikeUrl = feeds.FirstOrDefault(f => f.Name == "free_bike_status")?.Url;

        // Station-based handling
        if (stationInfoUrl is not null && stationStatusUrl is not null)
        {
            var infoJson = await http.GetStringAsync(stationInfoUrl, ct);
            var info = JsonSerializer.Deserialize<GbfsStationInformation>(infoJson);
            var stations = info?.Data?.Stations;
            if (stations is not null && stations.Count > 0)
            {
                var statusJson = await http.GetStringAsync(stationStatusUrl, ct);
                var status = JsonSerializer.Deserialize<GbfsStationStatus>(statusJson);
                var statuses = status?.Data?.Stations;
                var statusByStationId = statuses?.ToDictionary(s => s.StationId ?? string.Empty, s => s)
                    ?? [];

                var existingByStationId = await _db.MobilityStations
                    .Where(ms => ms.MobilityProviderId == provider.Id)
                    .ToDictionaryAsync(ms => ms.StationId, ct);

                foreach (var station in stations)
                {
                    var stationId = station.StationId ?? string.Empty;
                    var hasStatus = statusByStationId.TryGetValue(stationId, out var st);

                    if (existingByStationId.TryGetValue(stationId, out var existing))
                    {
                        existing.Name = station.Name ?? string.Empty;
                        existing.Latitude = station.Lat;
                        existing.Longitude = station.Lon;
                        existing.Capacity = station.Capacity > 0 ? station.Capacity : null;
                        existing.AvailableVehicles = hasStatus ? st!.NumBikesAvailable : 0;
                        existing.LastUpdated = DateTime.UtcNow;
                    }
                    else
                    {
                        _db.MobilityStations.Add(new MobilityStation
                        {
                            MobilityProviderId = provider.Id,
                            StationId = stationId,
                            Name = station.Name ?? string.Empty,
                            Latitude = station.Lat,
                            Longitude = station.Lon,
                            Capacity = station.Capacity > 0 ? station.Capacity : null,
                            AvailableVehicles = hasStatus ? st!.NumBikesAvailable : 0,
                            CountryId = await _placeMatching.DeriveCountryIdAsync(station.Lat, station.Lon, ct),
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }

                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("GBFS polled provider {ProviderId}: {Count} stations", provider.Id, stations.Count);
            }
        }

        // Free-floating vehicle handling
        if (freeBikeUrl is not null)
        {
            var bikeJson = await http.GetStringAsync(freeBikeUrl, ct);
            var bikeStatus = JsonSerializer.Deserialize<GbfsFreeBikeStatus>(bikeJson);
            var bikes = bikeStatus?.Data?.Bikes;
            if (bikes is not null && bikes.Count > 0)
            {
                var feedId = $"gbfs-{provider.Id}";
                int count = 0;
                foreach (var bike in bikes)
                {
                    if (bike.BikeId is null) continue;

                    _realtime.UpdateVehicleCache(feedId, new Contracts.VehicleResponse
                    {
                        VehicleId = bike.BikeId,
                        FeedId = feedId,
                        Latitude = bike.Lat,
                        Longitude = bike.Lon,
                        LastUpdated = DateTime.UtcNow,
                        IsRealtime = true
                    });
                    count++;
                }

                _logger.LogInformation("GBFS polled provider {ProviderId}: {Count} free-floating vehicles",
                    provider.Id, count);
            }
        }

        if (stationInfoUrl is null && stationStatusUrl is null && freeBikeUrl is null)
        {
            _logger.LogWarning("GBFS provider {ProviderId} has no recognized feeds", provider.Id);
        }
    }

    public async Task<int> UpsertStationsFromCustomFeedAsync(int providerId, List<Dictionary<string, object?>> records, CancellationToken ct = default)
    {
        var existingByStationId = await _db.MobilityStations
            .Where(ms => ms.MobilityProviderId == providerId)
            .ToDictionaryAsync(ms => ms.StationId, ct);

        async Task<int> GetCountryId(double lat, double lng)
        {
            return await _placeMatching.DeriveCountryIdAsync(lat, lng, ct);
        }

        int upserted = 0;
        foreach (var record in records)
        {
            var stationId = GetDictString(record, "station_id");
            var name = GetDictString(record, "name");
            var lat = GetDictDouble(record, "lat");
            var lng = GetDictDouble(record, "lon");

            if (stationId is null || name is null || lat is null || lng is null)
                continue;

            var capacity = GetDictInt(record, "capacity");
            var availableVehicles = GetDictInt(record, "availableVehicles") ?? 0;

            if (existingByStationId.TryGetValue(stationId, out var existing))
            {
                existing.Name = name;
                existing.Latitude = lat.Value;
                existing.Longitude = lng.Value;
                existing.Capacity = capacity > 0 ? capacity : null;
                existing.AvailableVehicles = availableVehicles;
                existing.LastUpdated = DateTime.UtcNow;
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
                    LastUpdated = DateTime.UtcNow
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

}
