using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

public class ExternalFeedSource : IFeedSource
{
    /// <summary>
    /// Hard ceiling on a single feed download. Feeds are buffered in memory before hashing, so an
    /// oversized or endless response would otherwise be an out-of-memory vector.
    /// </summary>
    public const long MaxFeedBytes = 512L * 1024 * 1024;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ExternalFeedSource> _logger;
    private readonly bool _allowPrivateNetworkUrls;

    public ExternalFeedSource(IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<ExternalFeedSource> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        // Escape hatch for developing against a locally hosted GTFS zip. Never enable in production:
        // it turns the feed importer back into an SSRF proxy for the server's own network.
        _allowPrivateNetworkUrls = configuration.GetValue("Feeds:AllowPrivateNetworkUrls", false);

        if (_allowPrivateNetworkUrls)
            _logger.LogWarning("Feeds:AllowPrivateNetworkUrls is enabled — feed URLs may target private addresses.");
    }

    public async Task<FeedFetchResult> FetchDataAsync(Feed feed, CancellationToken ct)
    {
        var url = feed.Url;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Feed {feed.Id} has no URL configured");

        if (!_allowPrivateNetworkUrls)
            EnsurePublicDestination(url);

        var http = _httpFactory.CreateClient("gtfs");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // A redirect can land somewhere the original URL check never saw.
        if (!_allowPrivateNetworkUrls
            && response.RequestMessage?.RequestUri is { } finalUri
            && finalUri.AbsoluteUri != url)
        {
            EnsurePublicDestination(finalUri.AbsoluteUri);
        }

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > MaxFeedBytes)
            throw new Exceptions.AppException(
                $"Feed exceeds the {MaxFeedBytes / (1024 * 1024)} MB download limit.", 413, "FEED_TOO_LARGE");

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var bytes = await ReadCappedAsync(response, ct);
        var etag = response.Headers.ETag?.Tag;
        DateTime? lastModified = response.Content.Headers.LastModified?.UtcDateTime;

        _logger.LogDebug("ExternalFeedSource: fetched {Length} bytes from {Url}", bytes.Length, url);
        return new FeedFetchResult(bytes, contentType, etag, lastModified);
    }

    /// <summary>Reads the body, aborting past <see cref="MaxFeedBytes"/> so a lying Content-Length cannot exhaust memory.</summary>
    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxFeedBytes)
                throw new Exceptions.AppException(
                    $"Feed exceeds the {MaxFeedBytes / (1024 * 1024)} MB download limit.", 413, "FEED_TOO_LARGE");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Rejects feed URLs that resolve to loopback, link-local, private or otherwise internal
    /// addresses. Feed URLs are operator-supplied, so without this the server is an SSRF proxy into
    /// its own network (including cloud instance-metadata endpoints).
    /// </summary>
    internal static void EnsurePublicDestination(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new Exceptions.AppException("Feed URL must be an absolute HTTP(S) URL.", 400, "INVALID_FEED_URL");

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = Dns.GetHostAddresses(uri.Host);
            }
            catch (SocketException)
            {
                throw new Exceptions.AppException($"Feed host '{uri.Host}' could not be resolved.", 400, "INVALID_FEED_URL");
            }
        }

        if (addresses.Length == 0 || addresses.Any(IsInternal))
            throw new Exceptions.AppException(
                $"Feed host '{uri.Host}' resolves to a non-public address.", 400, "INVALID_FEED_URL");
    }

    private static bool IsInternal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
                return IsInternal(address.MapToIPv4());

            // Unique local addresses (fc00::/7) and the unspecified address.
            var first = address.GetAddressBytes()[0];
            return (first & 0xFE) == 0xFC || address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true,                                          // 0.0.0.0/8
            10 => true,                                         // 10.0.0.0/8
            127 => true,                                        // loopback
            169 when octets[1] == 254 => true,                  // link-local, incl. 169.254.169.254 metadata
            172 when octets[1] >= 16 && octets[1] <= 31 => true, // 172.16.0.0/12
            192 when octets[1] == 168 => true,                  // 192.168.0.0/16
            >= 224 => true,                                     // multicast + reserved
            _ => false
        };
    }

    public string ComputeHash(Feed feed, byte[] data)
    {
        var hash = SHA1.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
