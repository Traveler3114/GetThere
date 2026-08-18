namespace TransitInfoAPI.Routing;

/// <summary>
/// Configuration for the routing/export layer. Everything market-specific lives here as data, not in
/// code: the spatial scope, the fallback timezone, the OSM extract, and the OTP endpoint. A second
/// city is a change to these values plus database rows — never a code change.
/// </summary>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>The rectangular scope the export bundles. Stops outside it are not exported.</summary>
    public BoundingBox Scope { get; set; } = new();

    /// <summary>
    /// IANA timezone used for <c>agency_timezone</c> when an operator publishes none. GTFS requires a
    /// timezone on every agency; this is the configured fallback, not a hardcoded Zagreb literal.
    /// </summary>
    public string Timezone { get; set; } = "Europe/Zagreb";

    public OsmExtractOptions OsmExtract { get; set; } = new();

    public OtpOptions Otp { get; set; } = new();

    public ExportOutputOptions Export { get; set; } = new();
}

public sealed class ExportOutputOptions
{
    /// <summary>
    /// If set, each rebuild also writes <c>gtfs.zip</c> and the GBFS JSON files here, so OTP can build
    /// its graph from a local file (and skip authenticating against the export endpoint). Null keeps
    /// the artifacts in memory only, served via <c>routing/</c>.
    /// </summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>A latitude/longitude rectangle. An unset (all-zero) box means "no spatial limit".</summary>
public sealed class BoundingBox
{
    public double MinLat { get; set; }
    public double MinLon { get; set; }
    public double MaxLat { get; set; }
    public double MaxLon { get; set; }

    /// <summary>
    /// True when no usable rectangle is configured, in which case the export applies no spatial
    /// filter. Keeping "unset" explicit avoids silently exporting an empty bundle from a zeroed box.
    /// </summary>
    public bool IsUnset => MaxLat <= MinLat || MaxLon <= MinLon;

    public bool Contains(double lat, double lon) =>
        IsUnset || (lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon);
}

public sealed class OsmExtractOptions
{
    /// <summary>Path to the cropped OSM extract fed to the OTP graph build (stored via FeedStorage).</summary>
    public string? Path { get; set; }

    /// <summary>Bounding box to crop the Geofabrik download to before the graph build.</summary>
    public BoundingBox BoundingBox { get; set; } = new();
}

public sealed class OtpOptions
{
    /// <summary>Base URL of the OTP2 GraphQL endpoint. Not internet-exposed; TransitInfoAPI is its only client.</summary>
    public string? Endpoint { get; set; }
}
