namespace TransitInfoAPI.Routing;

/// <summary>
/// Holds the most recently built routing artifacts (the GTFS bundle and the GBFS payloads) so they
/// are served as cached artifacts, regenerated on feed activation, rather than rebuilt on every
/// request. A single volatile reference swap is all the synchronization needed: readers either see
/// the previous complete set or the new complete set, never a half-written one.
/// </summary>
public sealed class RoutingExportCache
{
    private volatile Artifacts? _current;

    public Artifacts? Current => _current;

    public void Set(Artifacts artifacts) => _current = artifacts;

    public sealed record Artifacts(
        byte[] GtfsZip,
        string GbfsSystemInformation,
        string GbfsStationInformation,
        string GbfsStationStatus,
        DateTime BuiltAtUtc,
        int DroppedStopTimes);
}
