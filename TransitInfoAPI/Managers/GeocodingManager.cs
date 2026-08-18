using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Options;

using TransitInfoAPI.Routing;

namespace TransitInfoAPI.Managers;

public sealed class GeocodingOptions
{
    /// <summary>Azure Maps subscription key. Server-side only — must never reach the client or map WebView.</summary>
    public string? AzureMapsKey { get; set; }

    public string BaseUrl { get; set; } = "https://atlas.microsoft.com";

    /// <summary>ISO country filter, e.g. "HR". Comma-separated for several. Empty = unrestricted.</summary>
    public string CountrySet { get; set; } = "HR";

    public int Limit { get; set; } = 5;

    /// <summary>Bias/limit results to this viewport. Configuration, not a hardcoded Zagreb box.</summary>
    public BoundingBox Viewport { get; set; } = new();
}

/// <summary>One geocoded address candidate.</summary>
public sealed record GeocodeResult(string Label, double Lat, double Lon, double Score, string? CountryCode);

/// <summary>Thrown when geocoding is unconfigured or the upstream fails.</summary>
public sealed class GeocodingException(string message) : Exception(message);

/// <summary>
/// Address → coordinates via Azure Maps Search, called <b>server-side</b> so the subscription key
/// never reaches the client. This is an address lookup for a trip's start/end, deliberately not a
/// stop lookup — stop names geocode badly against an address database, and stops already live in the
/// canonical model. Biased to a configured viewport, not a hardcoded Zagreb box.
/// </summary>
public sealed class GeocodingManager(
    IHttpClientFactory httpClientFactory,
    IOptions<GeocodingOptions> options)
{
    public async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AzureMapsKey))
            throw new GeocodingException("Geocoding is not configured (Geocoding:AzureMapsKey).");
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var url = BuildUrl(o, query);

        var client = httpClientFactory.CreateClient("azuremaps");
        using var response = await client.GetAsync(url, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new GeocodingException($"Azure Maps responded {(int)response.StatusCode}.");

        return AzureMapsSearchParser.Parse(json);
    }

    private static string BuildUrl(GeocodingOptions o, string query)
    {
        var url =
            $"{o.BaseUrl.TrimEnd('/')}/search/address/json?api-version=1.0" +
            $"&subscription-key={Uri.EscapeDataString(o.AzureMapsKey!)}" +
            $"&query={Uri.EscapeDataString(query)}" +
            $"&limit={o.Limit.ToString(CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(o.CountrySet))
            url += $"&countrySet={Uri.EscapeDataString(o.CountrySet)}";

        // Azure Maps takes a bounding-box bias as topLeft (max lat, min lon) / btmRight (min lat, max lon).
        if (!o.Viewport.IsUnset)
        {
            url += $"&topLeft={Coord(o.Viewport.MaxLat)},{Coord(o.Viewport.MinLon)}" +
                   $"&btmRight={Coord(o.Viewport.MinLat)},{Coord(o.Viewport.MaxLon)}";
        }

        return url;
    }

    private static string Coord(double value) => value.ToString("0.0#####", CultureInfo.InvariantCulture);
}

/// <summary>Pure parser for Azure Maps Search Address responses — unit-testable without the service.</summary>
public static class AzureMapsSearchParser
{
    public static IReadOnlyList<GeocodeResult> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<GeocodeResult>();
        foreach (var r in results.EnumerateArray())
        {
            if (!r.TryGetProperty("position", out var pos) || pos.ValueKind != JsonValueKind.Object)
                continue;

            var lat = Num(pos, "lat");
            var lon = Num(pos, "lon");

            string? label = null, countryCode = null;
            if (r.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Object)
            {
                label = Str(addr, "freeformAddress");
                countryCode = Str(addr, "countryCode");
            }

            list.Add(new GeocodeResult(label ?? string.Empty, lat, lon, Num(r, "score"), countryCode));
        }
        return list;
    }

    private static double Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
