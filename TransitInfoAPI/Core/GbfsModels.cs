using System.Text.Json.Serialization;

namespace TransitInfoAPI.Core;

public sealed class GbfsRoot
{
    [JsonPropertyName("last_updated")]
    public long LastUpdated { get; set; }

    public int Ttl { get; set; }

    public GbfsData? Data { get; set; }

    public string? Version { get; set; }
}

public sealed class GbfsData
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AllLocales { get; set; }

    public GbfsLocaleFeeds? GetFeeds()
    {
        if (AllLocales is null || AllLocales.Count == 0) return null;

        // Prefer "en" if available (GBFS 2.x), otherwise first available locale (GBFS 3.0)
        if (AllLocales.TryGetValue("en", out var enElement))
            return System.Text.Json.JsonSerializer.Deserialize<GbfsLocaleFeeds>(enElement.GetRawText());

        var first = AllLocales.Values.First();
        return System.Text.Json.JsonSerializer.Deserialize<GbfsLocaleFeeds>(first.GetRawText());
    }
}

public sealed class GbfsLocaleFeeds
{
    public List<GbfsFeed>? Feeds { get; set; }
}

public sealed class GbfsFeed
{
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public sealed class GbfsStationInformation
{
    [JsonPropertyName("last_updated")]
    public long LastUpdated { get; set; }

    public int Ttl { get; set; }

    public StationInformationData? Data { get; set; }
}

public sealed class StationInformationData
{
    public List<GbfsStation>? Stations { get; set; }
}

public sealed class GbfsStation
{
    [JsonPropertyName("station_id")]
    public string? StationId { get; set; }

    public string? Name { get; set; }

    public double Lat { get; set; }

    public double Lon { get; set; }

    public int Capacity { get; set; }

    [JsonPropertyName("region_id")]
    public string? RegionId { get; set; }

    [JsonPropertyName("is_virtual_station")]
    public bool IsVirtualStation { get; set; }
}

public sealed class GbfsStationStatus
{
    [JsonPropertyName("last_updated")]
    public long LastUpdated { get; set; }

    public int Ttl { get; set; }

    public StationStatusData? Data { get; set; }
}

public sealed class StationStatusData
{
    public List<StationStatus>? Stations { get; set; }
}

public sealed class StationStatus
{
    [JsonPropertyName("station_id")]
    public string? StationId { get; set; }

    [JsonPropertyName("num_bikes_available")]
    public int NumBikesAvailable { get; set; }

    [JsonPropertyName("num_docks_available")]
    public int NumDocksAvailable { get; set; }

    [JsonPropertyName("is_installed")]
    public int IsInstalled { get; set; }

    [JsonPropertyName("is_renting")]
    public int IsRenting { get; set; }

    [JsonPropertyName("is_returning")]
    public int IsReturning { get; set; }
}

public sealed class GbfsFreeBikeStatus
{
    [JsonPropertyName("last_updated")]
    public long LastUpdated { get; set; }

    public int Ttl { get; set; }

    public FreeBikeStatusData? Data { get; set; }
}

public sealed class FreeBikeStatusData
{
    public List<FreeBike>? Bikes { get; set; }
}

public sealed class FreeBike
{
    [JsonPropertyName("bike_id")]
    public string? BikeId { get; set; }

    public double Lat { get; set; }

    public double Lon { get; set; }

    [JsonPropertyName("is_reserved")]
    public int IsReserved { get; set; }

    [JsonPropertyName("is_disabled")]
    public int IsDisabled { get; set; }

    [JsonPropertyName("vehicle_type_id")]
    public string? VehicleTypeId { get; set; }

    [JsonPropertyName("current_range_meters")]
    public double? CurrentRangeMeters { get; set; }
}
