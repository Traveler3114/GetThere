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
    public async Task<ActionResult<OperationResult>> Deactivate(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeactivateAsync(id, ct);
        if (!success) return NotFound(OperationResult.Fail("Feed not found."));
        return Ok(OperationResult.Ok("Feed deactivated."));
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

        _logger.LogInformation("Querying OTP GraphQL for stops to reconcile feed {FeedId}", feed.FeedId);

        try
        {
            var query = new { query = "{ stops { gtfsId name lat lon } }" };
            var response = await http.PostAsJsonAsync($"{otpBaseUrl}/otp/routers/default/index/graphql", query, ct);
            if (!response.IsSuccessStatusCode)
                return StatusCode(502, OperationResult.Fail($"OTP returned status {response.StatusCode}. Is OTP running?"));

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var stops = new List<(string stopId, string stopName, double lat, double lon)>();

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("stops", out var stopsArray))
            {
                var prefix = $"{feed.FeedId}:";
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

            _logger.LogInformation("Found {Count} stops for feed {FeedId} in OTP", stops.Count, feed.FeedId);

            if (stops.Count == 0)
                return Ok(OperationResult.Ok("No stops found for this feed in OTP. Has OTP finished building the graph?"));

            await _reconciliationService.ReconcileFeedStopsAsync(id, stops, ct);

            return Ok(OperationResult.Ok($"Reconciled {stops.Count} stops for feed '{feed.FeedId}'."));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, OperationResult.Fail($"Cannot reach OTP: {ex.Message}"));
        }
    }
}
