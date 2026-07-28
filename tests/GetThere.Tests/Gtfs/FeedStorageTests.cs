using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Services;

namespace GetThere.Tests.Gtfs;

/// <summary>
/// The feed-id containment rule. <c>FeedId</c> is caller-supplied and ends up in a filesystem path,
/// so this is the check that stops a feed from being written outside the feeds directory.
/// <para>
/// It was previously a private method on a manager with eight constructor dependencies, so it had
/// never been tested directly — only implied by the character restriction on the request contract,
/// which is a separate control that a future contract change could relax.
/// </para>
/// </summary>
public class FeedStorageTests
{
    private static readonly string ContentRoot =
        OperatingSystem.IsWindows() ? @"C:\app" : "/app";

    private static FeedStorage CreateStorage() => new(ContentRoot);

    [Theory]
    [InlineData("zet")]
    [InlineData("hzpp-2026")]
    [InlineData("A_B.c")]
    public void An_ordinary_feed_id_resolves_under_the_feeds_directory(string feedId)
    {
        var resolved = CreateStorage().DirectoryFor(feedId);

        var expectedRoot = Path.GetFullPath(Path.Combine(ContentRoot, "feeds")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(expectedRoot, resolved, StringComparison.Ordinal);
        Assert.EndsWith(feedId, resolved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("../../etc/passwd")]
    [InlineData("zet/../../escape")]
    [InlineData("..")]
    public void A_traversing_feed_id_is_refused(string feedId)
    {
        var ex = Assert.Throws<AppException>(() => CreateStorage().DirectoryFor(feedId));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("INVALID_FEED_ID", ex.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_feed_id_is_refused_rather_than_resolving_to_the_feeds_root(string feedId)
    {
        // Path.Combine with an empty segment returns the root itself, which is not a feed
        // directory — deleting or overwriting it would take every feed with it.
        Assert.Throws<AppException>(() => CreateStorage().DirectoryFor(feedId));
    }

    [Fact]
    public void An_absolute_feed_id_is_refused()
    {
        // Path.Combine discards the root entirely when the second segment is rooted, so without an
        // explicit check this would write to the absolute path the caller chose.
        var absolute = OperatingSystem.IsWindows() ? @"C:\windows\system32" : "/etc";

        Assert.Throws<AppException>(() => CreateStorage().DirectoryFor(absolute));
    }

    [Fact]
    public void The_zip_path_sits_inside_the_feed_directory()
    {
        var storage = CreateStorage();

        var zip = storage.ZipPathFor("zet");

        Assert.Equal(Path.Combine(storage.DirectoryFor("zet"), "gtfs.zip"), zip);
    }

    [Fact]
    public void The_zip_path_applies_the_same_containment_rule()
    {
        Assert.Throws<AppException>(() => CreateStorage().ZipPathFor("../escape"));
    }
}
