using Microsoft.AspNetCore.Mvc;
using transit_realtime;
using OpenTripPlannerAPI.Scrapers.HZPP.Services;

namespace OpenTripPlannerAPI.Controllers.HZPP;

[ApiController]
public class HzppRealtimeController : ControllerBase
{
    private readonly GtfsFeedStore _feedStore;

    public HzppRealtimeController(GtfsFeedStore feedStore)
    {
        _feedStore = feedStore;
    }

    /// <summary>GTFS-RT protobuf endpoint for OpenTripPlanner. GET /hzpp-rt</summary>
    [HttpGet("/hzpp-rt")]
    public IActionResult GetFeed()
    {
        var (bytes, _) = _feedStore.Read();

        if (bytes.Length == 0)
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
            bytes = empty.ToByteArray();
        }

        return File(bytes, "application/x-protobuf");
    }

    /// <summary>Human-readable status page. GET /status</summary>
    [HttpGet("/status")]
    [Produces("text/html")]
    public ContentResult GetStatus()
    {
        var (bytes, lastUpdated) = _feedStore.Read();
        var age = lastUpdated == DateTime.MinValue
            ? "never"
            : $"{(int)(DateTime.UtcNow - lastUpdated).TotalSeconds}s ago";

        return Content($"""
            <pre>
            HZPP-RT status
            Feed size   : {bytes.Length} bytes
            Last updated: {age}
            Endpoint    : /hzpp-rt
            </pre>
            """, "text/html");
    }
}
