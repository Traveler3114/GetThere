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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "GetThereAPI/1.0 (+https://getthere.app)");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var root = await JsonSerializer.DeserializeAsync<NextbikeRoot>(stream, _jsonOpts);

        if (root?.Countries is null)
            return [];

        var stations = new List<BikeStationDto>();

        foreach (var country in root.Countries)
        {
            if (country.Cities is null) continue;

            // Resolve the English country name: prefer the ISO code lookup so
            // that the value matches what is stored in the Countries DB table
            // (e.g. "HR" → "Croatia").  Fall back to the raw `name` field only
            // when the ISO code is missing or not in the lookup table.
            var countryName = (!string.IsNullOrEmpty(country.IsoCode) &&
                               _isoToName.TryGetValue(country.IsoCode, out var mapped))
                ? mapped
                : country.Name ?? "";

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
                        ProviderName   = provider.Name,
                        CountryName    = countryName,
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

        // Legacy fallback: existing local databases may still have Bajs / Nextbike
        // seeded without AdapterConfig. In that case default to Zagreb so bike
        // stations are visible on the map around the primary city area.
        if (string.IsNullOrWhiteSpace(provider.AdapterConfig))
        {
            if (provider.Name.Contains("Bajs", StringComparison.OrdinalIgnoreCase) ||
                provider.Name.Contains("Nextbike", StringComparison.OrdinalIgnoreCase))
                return $"{base_}?city=1172"; // Grad Zagreb

            return base_;
        }

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

    // ── ISO 3166-1 alpha-2 → English country name lookup ─────────────────────
    // Used to convert the Nextbike feed's `country` ISO code (e.g. "HR") into
    // the English name stored in the DB (e.g. "Croatia") so that
    // HasStationsInCountry comparisons succeed.  Extend as new countries are
    // added; the parser falls back to the raw `name` field if a code is absent.

    private static readonly Dictionary<string, string> _isoToName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["HR"] = "Croatia",
            ["SI"] = "Slovenia",
            ["AT"] = "Austria",
            ["BA"] = "Bosnia and Herzegovina",
            ["BE"] = "Belgium",
            ["BG"] = "Bulgaria",
            ["CH"] = "Switzerland",
            ["CY"] = "Cyprus",
            ["CZ"] = "Czech Republic",
            ["DE"] = "Germany",
            ["DK"] = "Denmark",
            ["EE"] = "Estonia",
            ["ES"] = "Spain",
            ["FI"] = "Finland",
            ["FR"] = "France",
            ["GB"] = "United Kingdom",
            ["GR"] = "Greece",
            ["HU"] = "Hungary",
            ["IE"] = "Ireland",
            ["IL"] = "Israel",
            ["IT"] = "Italy",
            ["JO"] = "Jordan",
            ["LT"] = "Lithuania",
            ["LU"] = "Luxembourg",
            ["LV"] = "Latvia",
            ["ME"] = "Montenegro",
            ["MK"] = "North Macedonia",
            ["MT"] = "Malta",
            ["NL"] = "Netherlands",
            ["NO"] = "Norway",
            ["PL"] = "Poland",
            ["PT"] = "Portugal",
            ["RO"] = "Romania",
            ["RS"] = "Serbia",
            ["SE"] = "Sweden",
            ["SK"] = "Slovakia",
            ["TR"] = "Turkey",
            ["UA"] = "Ukraine",
            ["US"] = "United States",
        };

    // ── Nextbike JSON model ───────────────────────────────────────────────────

    private class NextbikeRoot
    {
        public List<NextbikeCountry>? Countries { get; set; }
    }

    private class NextbikeCountry
    {
        public string? Name    { get; set; }

        /// <summary>ISO 3166-1 alpha-2 country code (e.g. "HR", "SI").</summary>
        [JsonPropertyName("country")]
        public string? IsoCode { get; set; }

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
