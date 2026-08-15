using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Services;

namespace GetThere.Tests;

/// <summary>
/// Feed URLs are operator-supplied and fetched server-side, so without a destination check the
/// importer is an SSRF proxy into TransitInfoAPI's own network — including cloud instance-metadata
/// endpoints at 169.254.169.254.
/// </summary>
public class FeedUrlSsrfTests
{
    [Theory]
    [InlineData("http://127.0.0.1/gtfs.zip")]
    [InlineData("http://localhost/gtfs.zip")]
    [InlineData("http://10.0.0.5/gtfs.zip")]
    [InlineData("http://172.16.3.4/gtfs.zip")]
    [InlineData("http://172.31.255.1/gtfs.zip")]
    [InlineData("http://192.168.1.10/gtfs.zip")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/gtfs.zip")]
    [InlineData("http://[::1]/gtfs.zip")]
    // Ranges the original check missed. 100.64/10 is carrier-grade NAT, which is where several
    // cloud providers put their internal service endpoints; 192.0.0.0/24 is IETF protocol
    // assignments; 198.18/15 is the benchmarking range. None of them are routable on the internet,
    // so a feed URL naming one is either a mistake or an attempt to reach something local.
    [InlineData("http://100.64.0.1/gtfs.zip")]
    [InlineData("http://100.127.255.254/gtfs.zip")]
    [InlineData("http://192.0.0.1/gtfs.zip")]
    [InlineData("http://198.18.0.1/gtfs.zip")]
    [InlineData("http://198.19.255.255/gtfs.zip")]
    public void Rejects_internal_destinations(string url)
    {
        var ex = Assert.Throws<AppException>(() => ExternalFeedSource.EnsurePublicDestination(url));
        Assert.Equal(400, ex.StatusCode);
    }

    /// <summary>
    /// Guards the edges of the ranges added above, so a future tightening cannot quietly swallow
    /// neighbouring public space. 100.63.x and 100.128.x sit either side of carrier-grade NAT, and
    /// 198.17/198.20 either side of the benchmarking range.
    /// </summary>
    [Theory]
    [InlineData("http://100.63.255.255/gtfs.zip")]
    [InlineData("http://100.128.0.1/gtfs.zip")]
    [InlineData("http://198.17.255.255/gtfs.zip")]
    [InlineData("http://198.20.0.1/gtfs.zip")]
    [InlineData("http://192.0.1.1/gtfs.zip")]
    public void Allows_addresses_just_outside_the_reserved_ranges(string url)
    {
        ExternalFeedSource.EnsurePublicDestination(url);
    }

    [Theory]
    [InlineData("ftp://example.com/gtfs.zip")]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void Rejects_non_http_schemes(string url)
    {
        Assert.Throws<AppException>(() => ExternalFeedSource.EnsurePublicDestination(url));
    }

    [Fact]
    public void Rejects_a_host_that_does_not_resolve()
    {
        Assert.Throws<AppException>(() =>
            ExternalFeedSource.EnsurePublicDestination("https://this-host-does-not-exist.invalid/gtfs.zip"));
    }

    [Theory]
    [InlineData("http://93.184.216.34/gtfs.zip")]
    [InlineData("https://8.8.8.8/gtfs.zip")]
    public void Allows_public_addresses(string url)
    {
        ExternalFeedSource.EnsurePublicDestination(url);
    }
}
