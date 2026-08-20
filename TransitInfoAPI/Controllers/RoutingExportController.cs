using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Routing;
using TransitInfoAPI.Routing.Export;

namespace TransitInfoAPI.Controllers;

/// <summary>
/// Serves the cached routing artifacts that the OTP graph build consumes: the whole-map GTFS bundle
/// and the GBFS feeds. OTP is TransitInfoAPI's only client here and is not internet-exposed, so these
/// stay authorized (like <c>realtime/tripupdates</c>) rather than public — deployment gives OTP a
/// service token or reaches this over an isolated network.
/// </summary>
[ApiController]
[Route("routing")]
[Authorize(Policy = "RoutingExport")]
public class RoutingExportController(RoutingExportCache cache) : ControllerBase
{
    /// <summary>The merged GTFS bundle. 404 until the first rebuild has run.</summary>
    [HttpGet("export/gtfs.zip")]
    public IActionResult Gtfs()
    {
        var current = cache.Current;
        if (current is null)
            return NotFound("No routing bundle has been built yet.");

        return File(current.GtfsZip, "application/zip", "gtfs.zip");
    }

    /// <summary>GBFS auto-discovery, built from the request host so OTP can follow it to the feeds.</summary>
    [HttpGet("gbfs/gbfs.json")]
    public IActionResult GbfsDiscovery()
    {
        if (cache.Current is null)
            return NotFound();

        var baseUrl = $"{Request.Scheme}://{Request.Host}/routing/gbfs";
        var feeds = new System.Text.Json.Nodes.JsonArray();
        foreach (var name in new[] { "system_information", "station_information", "station_status" })
        {
            feeds.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = name,
                ["url"] = $"{baseUrl}/{name}.json",
            });
        }

        var root = new System.Text.Json.Nodes.JsonObject
        {
            ["last_updated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["ttl"] = 60,
            ["version"] = "2.3",
            ["data"] = new System.Text.Json.Nodes.JsonObject
            {
                ["hr"] = new System.Text.Json.Nodes.JsonObject { ["feeds"] = feeds },
            },
        };
        return Content(root.ToJsonString(), "application/json");
    }

    [HttpGet("gbfs/system_information.json")]
    public IActionResult GbfsSystemInformation() => Payload(cache.Current?.GbfsSystemInformation);

    [HttpGet("gbfs/station_information.json")]
    public IActionResult GbfsStationInformation() => Payload(cache.Current?.GbfsStationInformation);

    [HttpGet("gbfs/station_status.json")]
    public IActionResult GbfsStationStatus() => Payload(cache.Current?.GbfsStationStatus);

    /// <summary>
    /// The normalized GTFS-RT trip-update feed OTP polls, with ids namespaced to the bundle. Built
    /// from the already-ingested realtime cache — one upstream fetch, done by the polling worker.
    /// </summary>
    [HttpGet("gtfs-rt")]
    public async Task<IActionResult> GtfsRealtime([FromServices] GtfsRealtimeExporter exporter, CancellationToken ct)
    {
        var bytes = await exporter.ExportAsync(ct);
        return File(bytes, "application/x-protobuf");
    }

    /// <summary>Immediate OSM extract refresh — the escape hatch that makes the daily worker skippable.</summary>
    [HttpPost("osm-refresh")]
    public async Task<IActionResult> OsmRefresh([FromServices] OsmExtractDownloader downloader, CancellationToken ct)
    {
        var results = await downloader.RefreshAllAsync(ct);
        return Ok(results);
    }

    private IActionResult Payload(string? json) =>
        json is null ? NotFound() : Content(json, "application/json");
}
