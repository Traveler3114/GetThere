using Microsoft.AspNetCore.Mvc;
using OpenTripPlannerAPI.Core;

namespace OpenTripPlannerAPI.Controllers;

[ApiController]
public sealed class RealtimeController : ControllerBase
{
    private readonly GtfsFeedStore _feedStore;
    private readonly ProtobufFeedBuilder _feedBuilder;

    public RealtimeController(GtfsFeedStore feedStore, ProtobufFeedBuilder feedBuilder)
    {
        _feedStore = feedStore;
        _feedBuilder = feedBuilder;
    }

    [HttpGet("/rt/{feedId}")]
    [HttpGet("/{feedId}-rt")]
    public IActionResult GetFeed(string? feedId = null)
    {
        var resolvedFeedId = string.IsNullOrWhiteSpace(feedId) ? "hzpp" : feedId;
        var snapshot = _feedStore.Read(resolvedFeedId);
        var bytes = snapshot?.Bytes;

        if (bytes is null || bytes.Length == 0)
            bytes = _feedBuilder.BuildEmptyFeedBytes();

        return File(bytes, "application/x-protobuf");
    }

    [HttpGet("/status")]
    [Produces("text/html")]
    public ContentResult GetStatus()
    {
        var feeds = _feedStore.ReadAll()
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var age = x.Value.LastUpdated == default
                    ? "never"
                    : $"{(int)(DateTime.UtcNow - x.Value.LastUpdated).TotalSeconds}s ago";

                return $"Feed        : {x.Key}\n" +
                       $"Size        : {x.Value.Bytes.Length} bytes\n" +
                       $"Last updated: {age}\n" +
                       $"Progress    : {x.Value.ProcessedItems}/{x.Value.TotalItems} processed\n" +
                       $"Updates     : {x.Value.ItemsWithUpdates}\n";
            })
            .ToList();

        var body = feeds.Count == 0
            ? "No feeds scraped yet."
            : string.Join("\n", feeds);

        return Content($"<pre>GTFS-RT status\n\n{body}\n</pre>", "text/html");
    }
}
