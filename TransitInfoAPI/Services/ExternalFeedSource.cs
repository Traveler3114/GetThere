using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

/// <summary>
/// Fetches a feed's raw bytes over HTTP, with the SSRF and size guards every caller needs.
/// <para>
/// Deliberately format-blind: <see cref="GtfsZipSource"/> composes it for GTFS archives, and the
/// realtime and mobility pollers use it directly for protobuf and GBFS.
/// </para>
/// </summary>
public class ExternalFeedSource
{
    /// <summary>
    /// Hard ceiling on a single feed download. Feeds are buffered in memory before hashing, so an
    /// oversized or endless response would otherwise be an out-of-memory vector.
    /// </summary>
    public const long MaxFeedBytes = 512L * 1024 * 1024;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ExternalFeedSource> _logger;
    private readonly bool _allowPrivateNetworkUrls;

    /// <summary>
    /// Host → <c>username:password</c>, from <c>Feeds:BasicAuth</c> (user-secrets, never the DB or
    /// git). The Croatian NAP (<c>b2b.promet-info.hr</c>) answers <c>401 WWW-Authenticate: Basic</c>,
    /// and <c>HttpClient</c> will not send credentials embedded in a URL, so the header is added
    /// here, keyed by the feed URL's host.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _basicAuth;

    public ExternalFeedSource(IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<ExternalFeedSource> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        // AllowPrivateNetworkUrls is the development escape hatch: a locally hosted GTFS zip is not
        // a public address, and without the flag the SSRF guard refuses it.
        _allowPrivateNetworkUrls = configuration.GetValue("Feeds:AllowPrivateNetworkUrls", false);

        _basicAuth = configuration.GetSection("Feeds:BasicAuth")
            .GetChildren()
            .ToDictionary(c => c.Key, c => c.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        if (_allowPrivateNetworkUrls)
            _logger.LogWarning("Feeds:AllowPrivateNetworkUrls is enabled — feed URLs may target private addresses.");
        if (_basicAuth.Count > 0)
            _logger.LogInformation("Basic auth configured for {Count} feed host(s): {Hosts}",
                _basicAuth.Count, string.Join(", ", _basicAuth.Keys));
    }

    public async Task<FeedFetchResult> FetchDataAsync(Feed feed, CancellationToken ct)
    {
        var url = feed.Url;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Feed {feed.Id} has no URL configured");

        if (!_allowPrivateNetworkUrls)
            EnsurePublicDestination(url);

        // A host with a configured credential is fetched with redirects followed by hand, so the
        // credential is never sent off the host it belongs to. Everything else stays on the plain
        // path below, whose auto-redirect never carries an Authorization header in the first place.
        using var response = BasicCredentialFor(url, out var credential)
            ? await FetchWithBasicAuthAsync(url, credential, ct)
            : await _httpFactory.CreateClient("gtfs").GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

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

    private bool BasicCredentialFor(string url, out string credential)
    {
        credential = string.Empty;
        if (_basicAuth.Count == 0) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!_basicAuth.TryGetValue(uri.Host, out var found)) return false;
        credential = found;
        return credential.Length > 0;
    }

    /// <summary>
    /// Fetches a feed whose host has a <c>Feeds:BasicAuth</c> credential, following redirects by
    /// hand so the credential stays on the host it belongs to.
    /// <para>
    /// This mirrors <c>CustomSourceEngine</c>'s off-host rule: the "gtfs-basic" client has
    /// <c>AllowAutoRedirect = false</c>, so every hop is decided here <em>before</em> the request
    /// goes out, and a redirect that would leave the configured host — or drop https to http, or
    /// move to a different port — refuses the whole fetch rather than sending the credential
    /// anywhere it was not meant for.
    /// </para>
    /// </summary>
    private async Task<HttpResponseMessage> FetchWithBasicAuthAsync(string url, string credential, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("gtfs-basic");
        var origin = new Uri(url);
        var current = origin;

        for (var hop = 0; ; hop++)
        {
            if (!_allowPrivateNetworkUrls)
                EnsurePublicDestination(current.AbsoluteUri);

            using var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));

            var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);

            var location = response.Headers.Location;
            if (!IsRedirect(response.StatusCode) || location is null)
                return response;

            var status = response.StatusCode;
            response.Dispose();

            if (hop >= MaxRedirects)
                throw new Exceptions.AppException(
                    $"Feed redirected more than {MaxRedirects} times starting at {origin.Host}.",
                    502, "TOO_MANY_REDIRECTS");

            if (!Uri.TryCreate(current, location, out var candidate)
                || candidate.Scheme is not ("http" or "https"))
            {
                throw new Exceptions.AppException(
                    $"{(int)status} from {current.Host} pointed at something that is not an HTTP(S) URL.",
                    502, "REDIRECT_INVALID");
            }

            if (!string.Equals(candidate.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
                throw new Exceptions.AppException(
                    $"{current.Host} redirected to {candidate.Host} and this feed carries a Basic credential — "
                    + "refusing to send it off-host. Point the feed at its final address instead.",
                    502, "REDIRECTED_OFF_HOST");

            if (origin.Scheme == "https" && candidate.Scheme == "http")
                throw new Exceptions.AppException(
                    $"{current.Host} redirected from https to http and this feed carries a Basic credential — "
                    + "refusing to send it in clear.",
                    502, "REDIRECT_DOWNGRADE");

            // Same host, same-or-better scheme, different port is still a different origin: ports on
            // one machine are different services. The single exception is the plain http→https
            // upgrade, where 80 becomes 443 precisely because the scheme improved.
            var schemeUpgrade = origin.Scheme == "http" && candidate.Scheme == "https"
                && origin.IsDefaultPort && candidate.IsDefaultPort;

            if (candidate.Port != origin.Port && !schemeUpgrade)
                throw new Exceptions.AppException(
                    $"{current.Host} redirected to port {candidate.Port} and this feed carries a Basic credential — "
                    + $"refusing to send it to a port other than {origin.Port}.",
                    502, "REDIRECTED_OFF_PORT");

            current = candidate;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private const int MaxRedirects = 5;

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

    /// <summary>
    /// Connects only to addresses <see cref="IsInternal"/> accepts, and is what actually enforces
    /// the SSRF rule.
    /// <para>
    /// <see cref="EnsurePublicDestination"/> alone could not: it resolves the name, and then
    /// <c>HttpClient</c> resolves it again when it connects. A record with a short TTL that answers
    /// public on the first lookup and <c>169.254.169.254</c> on the second walks straight past the
    /// check — the classic DNS-rebinding bypass. Deciding at connect time removes the window,
    /// because the address vetted here is the address the socket is opened to.
    /// </para>
    /// <para>
    /// It also covers redirects, which the pre-flight check could only ever handle after the fact.
    /// The callback runs for <em>every</em> connection the handler makes, so a 302 into the private
    /// network fails at the hop rather than being noticed once the body is already read.
    /// </para>
    /// </summary>
    public static async ValueTask<Stream> ConnectToPublicOnlyAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        // Every candidate must be public. Filtering to the acceptable ones rather than rejecting the
        // whole set on one bad entry would let a host publish one public and one private address and
        // still be reachable on the private one by a later connection.
        if (addresses.Length == 0 || addresses.Any(IsInternal))
            throw new Exceptions.AppException(
                $"Feed host '{host}' resolves to a non-public address.", 400, "INVALID_FEED_URL");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static bool IsInternal(IPAddress address)
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
            100 when octets[1] >= 64 && octets[1] <= 127 => true,// 100.64.0.0/10 carrier-grade NAT
            127 => true,                                        // loopback
            169 when octets[1] == 254 => true,                  // link-local, incl. 169.254.169.254 metadata
            172 when octets[1] >= 16 && octets[1] <= 31 => true, // 172.16.0.0/12
            192 when octets[1] == 0 && octets[2] == 0 => true,   // 192.0.0.0/24 IETF protocol assignments
            192 when octets[1] == 168 => true,                   // 192.168.0.0/16
            198 when octets[1] is 18 or 19 => true,              // 198.18.0.0/15 benchmarking
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
