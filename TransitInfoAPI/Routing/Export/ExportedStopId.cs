namespace TransitInfoAPI.Routing.Export;

/// <summary>
/// A whole-map GTFS bundle merges every active feed version into one dataset, but <c>RawStop</c> is
/// unique only on <c>(FeedVersionId, RawStopId)</c> — the same id string legitimately recurs across
/// versions, and the reconciliation backfill (<c>FeedManager.ReconcileAndBackfillAsync</c>) joins
/// stop times to raw stops on the bare string. So a bundle-wide export cannot use <c>RawStopId</c>
/// as a GTFS primary key as-is: it must be <b>namespaced by feed version</b>.
/// <para>
/// The encoding is deliberately <b>reversible</b>. GTFS-RT arrives keyed by the operator's original
/// stop/trip ids, so the RT re-serve endpoint (Step 6) has to translate original → exported. Because
/// the export keeps the original id beside its namespaced form, that translation is a lookup rather
/// than a heuristic — and <see cref="TryDecode"/> is the inverse it relies on.
/// </para>
/// </summary>
public static class ExportedStopId
{
    /// <summary>
    /// Encodes a feed-version-scoped stop id as <c>{feedVersionId}:{rawStopId}</c>. The prefix is a
    /// plain integer, so <see cref="TryDecode"/> can recover both parts by splitting on the first
    /// colon even when the original id itself contains colons.
    /// </summary>
    public static string Encode(int feedVersionId, string rawStopId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(feedVersionId);
        ArgumentNullException.ThrowIfNull(rawStopId);
        return $"{feedVersionId}:{rawStopId}";
    }

    /// <summary>
    /// Recovers <paramref name="feedVersionId"/> and <paramref name="rawStopId"/> from a value
    /// produced by <see cref="Encode"/>. Returns false for anything not in that shape (e.g. a bare
    /// canonical-station OnestopId, which is emitted un-namespaced as a parent station).
    /// </summary>
    public static bool TryDecode(string? exportedStopId, out int feedVersionId, out string rawStopId)
    {
        feedVersionId = 0;
        rawStopId = string.Empty;
        if (string.IsNullOrEmpty(exportedStopId))
            return false;

        var colon = exportedStopId.IndexOf(':');
        if (colon <= 0)
            return false;

        if (!int.TryParse(exportedStopId.AsSpan(0, colon), out feedVersionId))
            return false;

        rawStopId = exportedStopId[(colon + 1)..];
        return true;
    }
}
