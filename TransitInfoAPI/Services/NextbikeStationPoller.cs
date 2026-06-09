using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Contracts;
using TransitInfoAPI.Core;

namespace TransitInfoAPI.Services;

public sealed class NextbikeStationPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const int ProviderId = 1;
    private const string ProviderName = "Bajs / Nextbike";
    private const string FeedUrl = "https://nextbike.net/maps/nextbike-live.json";

    private readonly BikeStationCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<NextbikeStationPoller> _logger;

    public NextbikeStationPoller(
        BikeStationCache cache,
        IHttpClientFactory httpFactory,
        ILogger<NextbikeStationPoller> logger)
    {
        _cache = cache;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAsync(stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "GetThere/1.0 (+https://getthere.app)");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var root = await JsonSerializer.DeserializeAsync<NextbikeRoot>(stream, JsonOpts, ct);

            if (root?.Countries is null)
            {
                _logger.LogWarning("No Nextbike station data returned.");
                return;
            }

            List<BikeStationResponse> stations = [];
            foreach (var country in root.Countries)
            {
                if (country.Cities is null) continue;

                var countryName = (!string.IsNullOrEmpty(country.IsoCode) &&
                                   IsoToName.TryGetValue(country.IsoCode, out var mapped))
                    ? mapped
                    : country.Name ?? "";

                foreach (var city in country.Cities)
                {
                    if (city.Places is null) continue;

                    foreach (var place in city.Places)
                    {
                        stations.Add(new BikeStationResponse
                        {
                            StationId = place.Uid.ToString(),
                            Name = place.Name ?? "",
                            Lat = place.Lat,
                            Lon = place.Lng,
                            AvailableBikes = place.BikesAvailableToRent ?? place.Bikes,
                            Capacity = place.BikeRacks,
                            ProviderId = ProviderId,
                            ProviderName = ProviderName,
                            CountryName = countryName,
                        });
                    }
                }
            }

            _cache.Update(ProviderId, stations);
            _logger.LogInformation("Nextbike: loaded {Count} stations", stations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nextbike poll failed");
        }
    }

    private static readonly Dictionary<string, string> IsoToName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["HR"] = "Croatia", ["SI"] = "Slovenia", ["AT"] = "Austria",
            ["BA"] = "Bosnia and Herzegovina", ["BE"] = "Belgium",
            ["BG"] = "Bulgaria", ["CH"] = "Switzerland", ["CY"] = "Cyprus",
            ["CZ"] = "Czech Republic", ["DE"] = "Germany", ["DK"] = "Denmark",
            ["EE"] = "Estonia", ["ES"] = "Spain", ["FI"] = "Finland",
            ["FR"] = "France", ["GB"] = "United Kingdom", ["GR"] = "Greece",
            ["HU"] = "Hungary", ["IE"] = "Ireland", ["IL"] = "Israel",
            ["IT"] = "Italy", ["JO"] = "Jordan", ["LT"] = "Lithuania",
            ["LU"] = "Luxembourg", ["LV"] = "Latvia", ["ME"] = "Montenegro",
            ["MK"] = "North Macedonia", ["MT"] = "Malta", ["NL"] = "Netherlands",
            ["NO"] = "Norway", ["PL"] = "Poland", ["PT"] = "Portugal",
            ["RO"] = "Romania", ["RS"] = "Serbia", ["SE"] = "Sweden",
            ["SK"] = "Slovakia", ["TR"] = "Turkey", ["UA"] = "Ukraine",
            ["US"] = "United States",
        };

    private sealed class NextbikeRoot
    {
        public List<NextbikeCountry>? Countries { get; set; }
    }

    private sealed class NextbikeCountry
    {
        public string? Name { get; set; }

        [JsonPropertyName("country")]
        public string? IsoCode { get; set; }

        public List<NextbikeCity>? Cities { get; set; }
    }

    private sealed class NextbikeCity
    {
        public int Uid { get; set; }
        public string? Name { get; set; }
        public List<NextbikePlace>? Places { get; set; }
    }

    private sealed class NextbikePlace
    {
        public int Uid { get; set; }
        public string? Name { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int Bikes { get; set; }

        [JsonPropertyName("bikes_available_to_rent")]
        public int? BikesAvailableToRent { get; set; }

        [JsonPropertyName("bike_racks")]
        public int BikeRacks { get; set; }
    }
}
