namespace TransitInfoAPI.Routing.Export;

/// <summary>Which anchor an exported stop time resolved to — or that it could not be resolved.</summary>
public enum ExportedStopKind
{
    /// <summary>Resolved through <c>StopTime.RawStopEntityId</c> to a namespaced raw stop.</summary>
    RawStop,

    /// <summary>Resolved through <c>StopTime.CanonicalStationId</c> to a canonical parent station.</summary>
    CanonicalStation,

    /// <summary>Could not be resolved; the stop time must be dropped and logged.</summary>
    Dropped,
}

/// <summary>The outcome of resolving one stop time to an exportable <c>stop_id</c>.</summary>
public readonly record struct ResolvedStop(ExportedStopKind Kind, string? StopId, string? DropReason)
{
    public static ResolvedStop Raw(string stopId) => new(ExportedStopKind.RawStop, stopId, null);
    public static ResolvedStop Canonical(string stopId) => new(ExportedStopKind.CanonicalStation, stopId, null);
    public static ResolvedStop Drop(string reason) => new(ExportedStopKind.Dropped, null, reason);
}

/// <summary>
/// Resolves each exported stop time to a stop id via the exact-key chain the schema already
/// populates, in the order the plan mandates:
/// <list type="number">
///   <item>through <c>RawStopEntityId</c> when set → the namespaced raw stop (see <see cref="ExportedStopId"/>);</item>
///   <item>else through <c>CanonicalStationId</c> → the canonical station emitted as the stop;</item>
///   <item>else <b>drop and log</b> — an unresolvable reference is a data defect, and silently
///         emitting a dangling id produces a graph that builds and then mis-routes.</item>
/// </list>
/// This is deliberately an exact foreign-key resolution, never a proximity guess: replacing a key
/// with a geometric match can attach a departure to the wrong stop, which routes people to the wrong
/// place while looking perfectly healthy.
/// <para>
/// The resolver is pure — it takes only the lookups it needs — so it is unit-testable without a
/// database. The DB-backed exporter materializes these two maps once per export and feeds them in.
/// </para>
/// </summary>
public sealed class StopTimeResolver
{
    private readonly IReadOnlyDictionary<int, RawStopRef> _rawStopsById;
    private readonly IReadOnlyDictionary<int, string> _canonicalOnestopById;
    private readonly HashSet<(int FeedVersionId, string RawStopId)> _exportedRawStopKeys;

    /// <param name="rawStopsById">
    /// Exported raw stops keyed by their entity id, each carrying the <c>(FeedVersionId, RawStopId)</c>
    /// pair needed to build the namespaced exported id. A stop time whose <c>RawStopEntityId</c> is
    /// absent from this map — e.g. it points at a stop outside the export's spatial scope or an
    /// inactive one — is dropped rather than emitted dangling.
    /// </param>
    /// <param name="canonicalOnestopById">
    /// The <c>OnestopId</c> of each exported canonical station, keyed by entity id. Canonical stations
    /// are emitted as <c>location_type=1</c> parents keyed by OnestopId, so this is the stop id a
    /// canonical-resolved stop time takes.
    /// </param>
    public StopTimeResolver(
        IReadOnlyDictionary<int, RawStopRef> rawStopsById,
        IReadOnlyDictionary<int, string> canonicalOnestopById)
    {
        _rawStopsById = rawStopsById ?? throw new ArgumentNullException(nameof(rawStopsById));
        _canonicalOnestopById = canonicalOnestopById ?? throw new ArgumentNullException(nameof(canonicalOnestopById));
        _exportedRawStopKeys = [.. rawStopsById.Values.Select(r => (r.FeedVersionId, r.RawStopId))];
    }

    /// <param name="feedVersionId">The stop time's trip's feed version — the namespace its raw stop id lives in.</param>
    /// <param name="rawStopId">The bare GTFS stop id string the stop time always carries.</param>
    public ResolvedStop Resolve(int? rawStopEntityId, int? canonicalStationId, int feedVersionId, string? rawStopId)
    {
        // Arm 1: the exact backfilled foreign key. Handles the cross-feed Case A where a source's
        // stop id is defined in another feed version, which the same-version key (arm 3) cannot.
        if (rawStopEntityId is int rawId && _rawStopsById.TryGetValue(rawId, out var raw))
            return ResolvedStop.Raw(ExportedStopId.Encode(raw.FeedVersionId, raw.RawStopId));

        // Arm 2: the same-version (FeedVersionId, RawStopId) key. This is the raw stop's exported
        // identity by definition, so it resolves without depending on the reconciliation backfill
        // having populated RawStopEntityId — a stop time whose FK is null (backfill not yet run) still
        // resolves here. A cross-feed reference misses this (the id is not in this version) and falls
        // through to the canonical arm.
        if (!string.IsNullOrEmpty(rawStopId) && _exportedRawStopKeys.Contains((feedVersionId, rawStopId)))
            return ResolvedStop.Raw(ExportedStopId.Encode(feedVersionId, rawStopId));

        // Arm 3: the canonical station a source publishes against without owning the stop (Case A).
        if (canonicalStationId is int canonicalId && _canonicalOnestopById.TryGetValue(canonicalId, out var onestop))
            return ResolvedStop.Canonical(onestop);

        return ResolvedStop.Drop(
            $"stop time (feedVersion {feedVersionId}, rawStopId '{rawStopId}') resolved to no exported stop via FK, same-version key, or canonical station");
    }
}

/// <summary>The <c>(FeedVersionId, RawStopId)</c> pair an exported raw stop needs to be namespaced.</summary>
public readonly record struct RawStopRef(int FeedVersionId, string RawStopId);
