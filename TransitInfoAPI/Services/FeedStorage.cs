using TransitInfoAPI.Exceptions;

namespace TransitInfoAPI.Services;

/// <summary>
/// Resolves where a feed's files live on disk, refusing any id that would escape the feeds
/// directory.
/// <para>
/// This is a security control, not a path helper: <c>FeedId</c> is caller-supplied and flows into
/// <c>Path.Combine</c>, so without the containment check below a feed created as <c>../../etc</c>
/// writes wherever the process can reach. <c>CreateFeedRequest.FeedId</c> restricts the characters
/// as the first line of defence, and this is the second — the one that holds even if the contract
/// changes.
/// </para>
/// <para>
/// It lived as two private methods inside a 1,300-line manager whose constructor takes eight
/// dependencies, which made the rule impossible to test on its own. It is here so it can be.
/// </para>
/// </summary>
public sealed class FeedStorage
{
    private readonly string _feedsRoot;

    public FeedStorage(string contentRootPath)
    {
        _feedsRoot = Path.GetFullPath(Path.Combine(contentRootPath, "feeds"));
    }

    /// <summary>The directory holding a feed's files. Throws if <paramref name="feedId"/> escapes it.</summary>
    public string DirectoryFor(string feedId)
    {
        // Rejected before Path.Combine sees it: an absolute path or a rooted path would otherwise
        // replace the root entirely rather than combine with it, and an empty id resolves to the
        // feeds root itself, which is not a feed directory.
        if (string.IsNullOrWhiteSpace(feedId) || Path.IsPathRooted(feedId))
            throw new AppException($"Invalid feed id '{feedId}'.", 400, "INVALID_FEED_ID");

        var resolved = Path.GetFullPath(Path.Combine(_feedsRoot, feedId));

        if (!resolved.StartsWith(_feedsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AppException($"Invalid feed id '{feedId}'.", 400, "INVALID_FEED_ID");

        return resolved;
    }

    /// <summary>The path of a feed's downloaded GTFS archive.</summary>
    public string ZipPathFor(string feedId) => Path.Combine(DirectoryFor(feedId), "gtfs.zip");
}
