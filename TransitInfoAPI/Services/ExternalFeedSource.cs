using System.Security.Cryptography;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

public class ExternalFeedSource : IFeedSource
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ExternalFeedSource> _logger;

    public ExternalFeedSource(IHttpClientFactory httpFactory, ILogger<ExternalFeedSource> logger) { _httpFactory = httpFactory; _logger = logger; }

    public async Task<FeedFetchResult> FetchDataAsync(Feed feed, CancellationToken ct)
    {
        var url = feed.Url;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Feed {feed.Id} has no URL configured");

        var http = _httpFactory.CreateClient("gtfs");
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var etag = response.Headers.ETag?.Tag;
        DateTime? lastModified = response.Content.Headers.LastModified?.UtcDateTime;

        _logger.LogDebug("ExternalFeedSource: fetched {Length} bytes from {Url}", bytes.Length, url);
        return new FeedFetchResult(bytes, contentType, etag, lastModified);
    }

    public string ComputeHash(Feed feed, byte[] data)
    {
        var hash = SHA1.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
