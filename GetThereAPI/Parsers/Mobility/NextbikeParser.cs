using System.Text.Json;
using System.Text.Json.Serialization;
using GetThereShared.Dtos;
using GetThereAPI.Entities;

namespace GetThereAPI.Parsers.Mobility;

/// <summary>
/// Fetches and parses the Nextbike Live JSON API.
/// Feed URL:  {ApiBaseUrl}?city={cityUid}
/// cityUid is read from AdapterConfig: {"cityUid": 483}
/// </summary>
public class NextbikeParser : IMobilityParser
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<BikeStationDto>> ParseStationsAsync(
        MobilityProvider provider, HttpClient http)
    {
        var url = BuildUrl(provider);

        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var root = await JsonSerializer.DeserializeAsync<NextbikeRoot>(stream, _jsonOpts);

        if (root?.Countries is null)
            return [];

        var stations = new List<BikeStationDto>();

        foreach (var country in root.Countries)
        {
            if (country.Cities is null) continue;

            foreach (var city in country.Cities)
            {
                if (city.Places is null) continue;

                foreach (var place in city.Places)
                {
                    stations.Add(new BikeStationDto
                    {
                        StationId      = place.Uid.ToString(),
                        Name           = place.Name ?? "",
                        Lat            = place.Lat,
                        Lon            = place.Lng,
                        AvailableBikes = place.BikesAvailableToRent ?? place.Bikes,
                        Capacity       = place.BikeRacks,
                        ProviderId     = provider.Id,
                        ProviderName   = provider.Name
                    });
                }
            }
        }

        return stations;
    }

    // ── URL construction ─────────────────────────────────────────────────────

    private static string BuildUrl(MobilityProvider provider)
    {
        var base_ = provider.ApiBaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(provider.AdapterConfig))
            return base_;

        int? cityUid = null;
        try
        {
            using var doc = JsonDocument.Parse(provider.AdapterConfig);
            if (doc.RootElement.TryGetProperty("cityUid", out var prop))
                cityUid = prop.GetInt32();
        }
        catch (JsonException)
        {
            // Malformed AdapterConfig — fall back to base URL
        }

        return cityUid.HasValue
            ? $"{base_}?city={cityUid.Value}"
            : base_;
    }

    // ── Nextbike JSON model ───────────────────────────────────────────────────

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
        public int Uid { get; set; }
        public string? Name { get; set; }
        public List<NextbikePlace>? Places { get; set; }
    }

    private class NextbikePlace
    {
        public int     Uid   { get; set; }
        public string? Name  { get; set; }
        public double  Lat   { get; set; }
        public double  Lng   { get; set; }
        public int     Bikes { get; set; }

        [JsonPropertyName("bikes_available_to_rent")]
        public int? BikesAvailableToRent { get; set; }

        [JsonPropertyName("bike_racks")]
        public int BikeRacks { get; set; }
    }
}
