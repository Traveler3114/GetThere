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
    public Dictionary<string, GbfsLocaleFeeds>? En { get; set; }
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
