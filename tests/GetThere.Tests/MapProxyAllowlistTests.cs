using GetThereAPI.Managers;

namespace GetThere.Tests;

/// <summary>
/// The map proxy forwards reads to TransitInfoAPI authenticated as the **service account**, so the
/// allowlist is the only thing standing between a user holding <c>map.view</c> and TransitInfoAPI's
/// entire admin surface. It was verified by hand when written; these tests are what stop it being
/// widened by accident later.
/// </summary>
public class MapProxyAllowlistTests
{
    [Theory]
    [InlineData("stations")]
    [InlineData("routes")]
    [InlineData("mobility/stations")]
    [InlineData("realtime/vehicles")]
    [InlineData("realtime/alerts")]
    [InlineData("map/transport-types")]
    [InlineData("stations/1/departures")]
    [InlineData("stations/4821/operators")]
    public void Allows_the_paths_the_map_page_needs(string path)
    {
        Assert.True(MapManager.IsAllowedUpstreamPath(path));
    }

    [Theory]
    // TransitInfoAPI's own admin surface — the whole point of the allowlist.
    [InlineData("users")]
    [InlineData("roles")]
    [InlineData("feeds")]
    [InlineData("agencies")]
    [InlineData("operators")]
    [InlineData("countries")]
    [InlineData("places")]
    [InlineData("reconciliation/candidates")]
    [InlineData("feedversions")]
    [InlineData("auth/login")]
    public void Blocks_everything_else(string path)
    {
        Assert.False(MapManager.IsAllowedUpstreamPath(path));
    }

    [Theory]
    [InlineData("../auth/login")]
    [InlineData("stations/../users")]
    [InlineData("stations/1/../../users")]
    [InlineData("./users")]
    [InlineData("/stations")]          // leading slash would double up on the forwarded path
    [InlineData("stations/")]          // trailing slash
    [InlineData("stations?format=geojson")] // query belongs in the query string, not the path
    public void Rejects_traversal_and_malformed_paths(string path)
    {
        Assert.False(MapManager.IsAllowedUpstreamPath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_paths(string path)
    {
        Assert.False(MapManager.IsAllowedUpstreamPath(path));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(MapManager.IsAllowedUpstreamPath(null!));
    }

    [Theory]
    // The station patterns take a numeric id; anything else must not slip through.
    [InlineData("stations/abc/departures")]
    [InlineData("stations/1;drop/departures")]
    [InlineData("stations/1/departures/extra")]
    [InlineData("stations/1/delete")]
    public void Station_sub_paths_are_constrained(string path)
    {
        Assert.False(MapManager.IsAllowedUpstreamPath(path));
    }

    [Theory]
    // Anchoring: a permitted segment must not make a longer path permitted.
    [InlineData("stationsextra")]
    [InlineData("xstations")]
    [InlineData("routes/1")]
    [InlineData("realtime/vehicles/all")]
    public void Patterns_are_anchored(string path)
    {
        Assert.False(MapManager.IsAllowedUpstreamPath(path));
    }
}
