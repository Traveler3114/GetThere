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
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _providerLocks = new();

    public MobilityManager(TransitDbContext db, IHttpClientFactory httpFactory, ILogger<MobilityManager> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
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
                            Capacity = place.BikeRacks,
                            AvailableVehicles = place.BikesAvailableToRent ?? place.Bikes,
                            CountryId = provider.Operator.CountryId,
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
