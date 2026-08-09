namespace TransitInfoAPI.Core;

/// <summary>
/// A raw HTTP fetch of a feed's bytes, before anything knows what format they are in.
/// <para>
/// Used directly by the realtime and mobility pollers, which consume protobuf and GBFS JSON and have
/// no document to build. The GTFS-static path goes through <see cref="ITransitSource"/> instead.
/// </para>
/// </summary>
public record FeedFetchResult(
    byte[] Data,
    string? ContentType,
    string? ETag,
    DateTime? LastModified
);
