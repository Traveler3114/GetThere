using TransitInfoAPI.Entities;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Core;

/// <summary>
/// Where a feed's data comes from, and how it becomes a <see cref="TransitDocument"/>.
/// <para>
/// The two methods mirror the two stages the importer has always had, and they are separate for the
/// same reason they always were: <see cref="FetchAsync"/> runs on the polling cadence and its answer
/// is a hash, so an unchanged feed costs a download and nothing else. <see cref="OpenAsync"/> only
/// runs once that hash turns out to be new.
/// </para>
/// <para>
/// The handoff between them is the artifact each source persists to the feed's storage directory —
/// a GTFS zip today, whatever a custom source needs tomorrow. Nothing is carried in memory across
/// the two calls, so an import can be re-run from disk without re-fetching, which is what
/// <c>POST /feeds/versions/{id}/reconcile</c> and a manual re-import already depend on.
/// </para>
/// </summary>
public interface ITransitSource
{
    /// <summary>Stable identifier for this source kind, used in import logs.</summary>
    string Kind { get; }

    bool CanHandle(Feed feed);

    /// <summary>
    /// Downloads the feed and persists it, returning the content hash used to decide whether
    /// anything changed. Returns null when there is nothing to import.
    /// </summary>
    Task<SourceFetchResult?> FetchAsync(Feed feed, CancellationToken ct);

    /// <summary>
    /// Reads the persisted artifact into a normalized document. The result owns any file handles or
    /// temporary copies it opened and releases them on disposal — callers must
    /// <c>await using</c> it.
    /// </summary>
    Task<SourceReadResult> OpenAsync(Feed feed, int feedVersionId, CancellationToken ct);
}

public record SourceFetchResult(string ContentHash, long ByteCount, string? ContentType);

/// <summary>
/// A document plus the live resources needed to stream its stop times.
/// </summary>
public sealed class SourceReadResult : IAsyncDisposable
{
    public TransitDocument? Document { get; init; }

    /// <summary>Non-empty when the source could not produce a usable document. Reported verbatim as the import error.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// A factory rather than a sequence, because stop times are read more than once — the bulk
    /// import needs them, and so do shape auto-generation and manual-edit carry-forward. Each call
    /// starts a fresh pass.
    /// </summary>
    public Func<IAsyncEnumerable<List<RawStopTimeRecord>>>? StopTimes { get; init; }

    public Func<ValueTask>? OnDispose { get; init; }

    public bool Failed => Errors.Count > 0 || Document is null;

    public static SourceReadResult Fail(params string[] errors) => new() { Errors = errors };

    public async ValueTask DisposeAsync()
    {
        if (OnDispose is not null)
            await OnDispose();
    }
}
