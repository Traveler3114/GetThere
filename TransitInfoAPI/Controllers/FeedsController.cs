using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;
using TransitInfoAPI.Services.Otp;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class FeedsController : ControllerBase
{
    private readonly FeedService _feedService;
    private readonly FeedImportService _feedImportService;
    private readonly ReconciliationService _reconciliationService;
    private readonly OtpManagerService _otpManager;
    private readonly TransitDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<FeedsController> _logger;

    public FeedsController(
        FeedService feedService,
        FeedImportService feedImportService,
        ReconciliationService reconciliationService,
        OtpManagerService otpManager,
        TransitDbContext db,
        IConfiguration config,
        ILogger<FeedsController> logger)
    {
        _feedService = feedService;
        _feedImportService = feedImportService;
        _reconciliationService = reconciliationService;
        _otpManager = otpManager;
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetAll(CancellationToken ct = default)
    {
        var feeds = await _feedService.GetAllAsync(ct);
        return Ok(OperationResult<List<FeedDto>>.Ok(feeds));
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<FeedDto>>> Create(
        [FromQuery] int operatorId,
        [FromQuery] FeedType feedType,
        [FromQuery] SourceType sourceType,
        [FromQuery] string feedId,
        [FromQuery] string? externalUrl,
        [FromQuery] int refreshIntervalSeconds = 3600,
        CancellationToken ct = default)
    {
        var feed = await _feedService.CreateAsync(operatorId, feedType, sourceType, feedId, externalUrl, refreshIntervalSeconds, ct);
        var dto = await _feedService.GetByIdAsync(feed.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { }, OperationResult<FeedDto>.Ok(dto!));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult>> Update(int id, [FromBody] Feed updated, CancellationToken ct = default)
    {
        var (success, message) = await _feedService.UpdateAsync(id, updated, ct);
        if (!success) return NotFound(OperationResult.Fail(message!));
        return Ok(OperationResult.Ok("Feed updated."));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeleteAsync(id, ct);
        if (!success) return NotFound(OperationResult.Fail("Feed not found."));
        return Ok(OperationResult.Ok("Feed deleted."));
    }

    [HttpPost("{id}/import")]
    public async Task<ActionResult<OperationResult>> Import(int id, CancellationToken ct = default)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { id }, ct);
        if (feed is null)
            return NotFound(OperationResult.Fail("Feed not found."));

        if (feed.FeedType != FeedType.GTFSStatic)
            return BadRequest(OperationResult.Fail("Only GTFS Static feeds can be imported."));

        _logger.LogInformation("Starting import for feed {FeedId} ({Id})", feed.FeedId, id);

        try
        {
            await _feedImportService.ImportGtfsStaticAsync(id, ct);
            await _otpManager.GenerateConfigAsync(ct);
            _logger.LogInformation("Import complete for feed {FeedId}", feed.FeedId);
            return Ok(OperationResult.Ok("Import complete. OTP config regenerated."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed for feed {FeedId}", feed.FeedId);
            return StatusCode(500, OperationResult.Fail($"Import failed: {ex.Message}"));
        }
    }

    [HttpPost("{id}/reconcile")]
    public async Task<ActionResult<OperationResult>> Reconcile(int id, CancellationToken ct = default)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { id }, ct);
        if (feed is null)
            return NotFound(OperationResult.Fail("Feed not found."));

        if (feed.LastSuccessful is null)
            return BadRequest(OperationResult.Fail("Feed has not been imported yet. Import first."));

        var otpBaseUrl = _config["Otp:InstanceBaseUrl"] ?? "http://localhost:8080";
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        _logger.LogInformation("Querying OTP GraphQL for stops and routes to reconcile feed {FeedId}", feed.FeedId);

        try
        {
            var query = new { query = "{ stops { gtfsId name lat lon } routes { gtfsId shortName longName mode color textColor } }" };
            var response = await http.PostAsJsonAsync($"{otpBaseUrl}/otp/routers/default/index/graphql", query, ct);
            if (!response.IsSuccessStatusCode)
                return StatusCode(502, OperationResult.Fail($"OTP returned status {response.StatusCode}. Is OTP running?"));

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var stops = new List<(string stopId, string stopName, double lat, double lon)>();
            var routes = new List<(string gtfsId, string shortName, string longName, string mode, string? color, string? textColor)>();

            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var prefix = $"{feed.FeedId}:";

                if (data.TryGetProperty("stops", out var stopsArray))
                {
                    foreach (var s in stopsArray.EnumerateArray())
                    {
                        var gtfsId = s.TryGetProperty("gtfsId", out var g) ? g.GetString() ?? "" : "";
                        if (!gtfsId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        stops.Add((
                            gtfsId,
                            s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            s.TryGetProperty("lat", out var lat) ? lat.GetDouble() : 0,
                            s.TryGetProperty("lon", out var lon) ? lon.GetDouble() : 0
                        ));
                    }
                }

                if (data.TryGetProperty("routes", out var routesArray))
                {
                    foreach (var r in routesArray.EnumerateArray())
                    {
                        var gtfsId = r.TryGetProperty("gtfsId", out var g) ? g.GetString() ?? "" : "";
                        if (!gtfsId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        routes.Add((
                            gtfsId,
                            r.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? "" : "",
                            r.TryGetProperty("longName", out var ln) ? ln.GetString() ?? "" : "",
                            r.TryGetProperty("mode", out var m) ? m.GetString() ?? "" : "",
                            r.TryGetProperty("color", out var c) ? c.GetString() : null,
                            r.TryGetProperty("textColor", out var tc) ? tc.GetString() : null
                        ));
                    }
                }
            }

            await _reconciliationService.ReconcileFeedRoutesAsync(id, routes, ct);
            _logger.LogInformation("Found {StopCount} stops and {RouteCount} routes for feed {FeedId} in OTP", stops.Count, routes.Count, feed.FeedId);

            if (stops.Count == 0)
                return Ok(OperationResult.Ok("No stops found for this feed in OTP. Has OTP finished building the graph?"));

            await _reconciliationService.ReconcileFeedStopsAsync(id, stops, ct);

            return Ok(OperationResult.Ok($"Reconciled {stops.Count} stops and {routes.Count} routes for feed '{feed.FeedId}'."));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, OperationResult.Fail($"Cannot reach OTP: {ex.Message}"));
        }
    }
}
