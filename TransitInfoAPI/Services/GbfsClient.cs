using System.Text.Json;

using GetThereShared.Contracts;
using TransitInfoAPI.Core;

namespace TransitInfoAPI.Services;

public sealed class GbfsClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GbfsClient> _logger;

    public GbfsClient(IHttpClientFactory httpFactory, ILogger<GbfsClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<List<BikeStationResponse>> FetchStationsAsync(
        string autoDiscoveryUrl,
        int providerId,
        string providerName,
        CancellationToken ct = default)
    {
        using var http = _httpFactory.CreateClient();

        var gbfsRoot = await FetchAsync<GbfsRoot>(http, autoDiscoveryUrl, ct);
        if (gbfsRoot?.Data?.En is null)
        {
            _logger.LogWarning("GBFS discovery returned no data for {Url}", autoDiscoveryUrl);
            return [];
        }

        var feeds = gbfsRoot.Data.En.GetValueOrDefault("en")?.Feeds
                 ?? gbfsRoot.Data.En.Values.FirstOrDefault()?.Feeds;

        var stationInfoUrl = feeds?.FirstOrDefault(f => f.Name == "station_information")?.Url;
        var stationStatusUrl = feeds?.FirstOrDefault(f => f.Name == "station_status")?.Url;

        if (string.IsNullOrWhiteSpace(stationInfoUrl) || string.IsNullOrWhiteSpace(stationStatusUrl))
        {
            _logger.LogWarning("GBFS discovery missing station_information or station_status feeds");
            return [];
        }

        var info = await FetchAsync<GbfsStationInformation>(http, stationInfoUrl, ct);
        var status = await FetchAsync<GbfsStationStatus>(http, stationStatusUrl, ct);

        if (info?.Data?.Stations is null)
            return [];

        var statusMap = status?.Data?.Stations
            ?.Where(s => s.StationId is not null)
            .GroupBy(s => s.StationId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return info.Data.Stations
            .Where(s => !s.IsVirtualStation)
            .Select(s =>
            {
                var live = s.StationId is not null ? statusMap?.GetValueOrDefault(s.StationId) : null;
                return new BikeStationResponse
                {
                    StationId = s.StationId ?? "",
                    Name = s.Name ?? "",
                    Lat = s.Lat,
                    Lon = s.Lon,
                    AvailableBikes = live?.NumBikesAvailable ?? 0,
                    Capacity = s.Capacity,
                    ProviderId = providerId,
                    ProviderName = providerName,
                    CountryName = "",
                };
            })
            .ToList();
    }

    private static async Task<T?> FetchAsync<T>(
        HttpClient http,
        string url,
        CancellationToken ct) where T : class
    {
        try
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }
}
