using Microsoft.AspNetCore.Mvc;
using OpenTripPlannerAPI.Core;
using transit_realtime;

namespace OpenTripPlannerAPI.Controllers;

[ApiController]
public class RealtimeController : ControllerBase
{
    private readonly GtfsFeedStore _feedStore;
    private readonly IConfiguration _configuration;

    public RealtimeController(GtfsFeedStore feedStore, IConfiguration configuration)
    {
        _feedStore = feedStore;
        _configuration = configuration;
    }

    [HttpGet(RealtimeRouteConventions.GenericFeedRoute)]
    public IActionResult GetFeed(string feedId)
    {
        var state = _feedStore.Read(feedId);
        var bytes = state.Bytes.Length > 0 ? state.Bytes : BuildEmptyFeed();
        return File(bytes, "application/x-protobuf");
    }

    [HttpGet(RealtimeRouteConventions.LegacyHzppRoute)]
    public IActionResult GetHzppCompatibilityFeed()
    {
        var feedId = _configuration["Scrapers:Hzpp:FeedId"]
                     ?? _configuration["Scrapers:HZPP:FeedId"]
                     ?? "hzpp";
        return GetFeed(feedId);
    }

    [HttpGet(RealtimeRouteConventions.LegacyFeedBySuffixRoute)]
    public IActionResult GetFeedByLegacyPath(string feedId)
        => GetFeed(feedId);

    [HttpGet("/status")]
    [Produces("text/html")]
    public ContentResult GetStatus()
    {
        var feeds = _feedStore.ReadAll();

        var lines = new List<string>
        {
            "GTFS-RT scraper host status",
            ""
        };

        if (feeds.Count == 0)
        {
            lines.Add("No feeds yet.");
        }
        else
        {
            foreach (var (feedId, state) in feeds.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var age = state.LastUpdated == DateTime.MinValue
                    ? "never"
                    : $"{(int)(DateTime.UtcNow - state.LastUpdated).TotalSeconds}s ago";

                lines.Add($"Feed        : {feedId}");
                lines.Add($"Endpoint    : /rt/{feedId}");
                lines.Add($"Size        : {state.Bytes.Length} bytes");
                lines.Add($"Last updated: {age}");
                lines.Add($"Healthy     : {state.IsHealthy}");
                if (!string.IsNullOrWhiteSpace(state.LastError))
                    lines.Add($"Last error  : {state.LastError}");
                lines.Add("");
            }
        }

        return Content($"<pre>{string.Join(Environment.NewLine, lines)}</pre>", "text/html");
    }

    [HttpGet("/status/{feedId}")]
    public IActionResult GetFeedStatus(string feedId)
    {
        var state = _feedStore.Read(feedId);
        return Ok(new
        {
            feedId,
            sizeBytes = state.Bytes.Length,
            lastUpdatedUtc = state.LastUpdated == DateTime.MinValue ? (DateTime?)null : state.LastUpdated,
            healthy = state.IsHealthy,
            lastError = state.LastError,
            endpoint = $"/rt/{feedId}"
        });
    }

    private static byte[] BuildEmptyFeed()
    {
        var empty = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Incrementality = FeedHeader.Types.Incrementality.FullDataset,
                Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };

        return empty.ToByteArray();
    }
}
