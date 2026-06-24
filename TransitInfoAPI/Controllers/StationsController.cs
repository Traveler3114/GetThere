using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class StationsController : ControllerBase
{
    private readonly StationManager _stationService;
    private readonly TransitDbContext _db;
    private readonly IConfiguration _config;

    public StationsController(StationManager stationManager, TransitDbContext db, IConfiguration config)
    {
        _stationService = stationManager;
        _db = db;
        _config = config;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int? countryId,
        [FromQuery] string? format = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        if (format == "geojson")
        {
            var query = _db.CanonicalStations
                .Include(cs => cs.Country)
                .Where(cs => cs.IsActive && cs.StationType == StationType.Stop)
                .AsQueryable();

            if (countryId.HasValue)
                query = query.Where(cs => cs.CountryId == countryId.Value);

            if (lat is not null && lon is not null && radiusKm is not null)
            {
                var latRange = radiusKm.Value / 111.0;
                var lonRange = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
                query = query.Where(cs =>
                    cs.Latitude >= lat.Value - latRange &&
                    cs.Latitude <= lat.Value + latRange &&
                    cs.Longitude >= lon.Value - lonRange &&
                    cs.Longitude <= lon.Value + lonRange);
            }

            var allStations = await query
                .OrderBy(cs => cs.Id)
                .Take(5000)
                .Select(StationMapper.ToResponseExpression)
                .ToListAsync(ct);

            var fc = GeoJsonGeometry.ToPointCollection(allStations,
                s => s.Latitude, s => s.Longitude,
                s => new Dictionary<string, object?>
                {
                    ["id"] = s.Id,
                    ["globalId"] = s.GlobalId,
                    ["onestopId"] = s.OnestopId,
                    ["name"] = s.Name,
                    ["stationType"] = s.StationType,
                    ["routeType"] = s.PrimaryRouteType,
                    ["primaryRouteType"] = s.PrimaryRouteType,
                    ["countryName"] = s.CountryName,
                    ["cityName"] = s.CityName
                });
            return Ok(fc);
        }

        var result = await _stationService.GetAllAsync(lat, lon, radiusKm, countryId, page, perPage, ct);
        var total = await _stationService.GetTotalCountAsync(lat, lon, radiusKm, countryId, null, ct: ct);
        return Ok(new Paginated<StationResponse>(result, total, page, perPage));
    }

    // TODO: Before Phase 3 (public launch), this endpoint is used by the
    // reconciliation-map.html search UI (Task 4.4) and exposes station data
    // used to locate reconciliation candidates. Must be restricted to admin-only.
    [HttpGet("search")]
    public async Task<ActionResult<Paginated<StationResponse>>> Search(
        [FromQuery] string? q,
        [FromQuery] RouteType? routeType,
        [FromQuery] int? countryId,
        [FromQuery] string? countryName = null,
        [FromQuery] string? stationType = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.SearchAsync(q, routeType, countryId, countryName, stationType, page, perPage, ct);
        var total = await _stationService.GetTotalCountAsync(null, null, null, countryId, countryName, stationType, ct);
        return Ok(new Paginated<StationResponse>(result, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<StationResponse>> GetById(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<StationResponse>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByOnestopIdAsync(onestopId, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<List<StationOperatorResponse>>> GetOperators(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        var operators = await _stationService.GetOperatorsAsync(station.OnestopId, ct);
        return Ok(new Paginated<StationOperatorResponse>(operators, operators.Count, 1, operators.Count));
    }

    [HttpGet("{id}/routes")]
    public async Task<ActionResult<List<RouteResponse>>> GetRoutes(int id, CancellationToken ct = default)
    {
        var routeIds = await _db.StopTimes
            .Where(st => st.CanonicalStationId == id)
            .Where(st => st.Trip.CanonicalRouteId != null)
            .Select(st => st.Trip.CanonicalRouteId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var routes = await _db.CanonicalRoutes
            .Where(r => routeIds.Contains(r.Id))
            .Take(500)
            .Select(RouteMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(routes);
    }

    [HttpGet("by-global/{globalId}")]
    public async Task<ActionResult<StationResponse>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByGlobalIdAsync(globalId, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpPost("{id}/rematch-place")]
    public async Task<IActionResult> RematchPlace(int id, CancellationToken ct = default)
    {
        var station = await _db.CanonicalStations.FindAsync([id], ct);
        if (station is null) return NotFound();
        var placeMatching = HttpContext.RequestServices.GetRequiredService<PlaceMatchingManager>();
        await placeMatching.RematchStationAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id}/departures")]
    public async Task<ActionResult<List<DepartureResponse>>> GetDepartures(
        int id,
        [FromQuery] DateTime? from = null,
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        var departures = await _stationService.GetDeparturesAsync(id, from, count, ct);
        return Ok(new Paginated<DepartureResponse>(departures, departures.Count, 1, departures.Count));
    }

    // TODO: Before Phase 3 (public launch), this endpoint exposes reconciliation
    // internals on the public-facing API. It must be restricted to admin-only access.
    [HttpGet("{id}/reconciliation-detail")]
    public async Task<ActionResult<StationReconciliationDetailResponse>> GetReconciliationDetail(int id, CancellationToken ct = default)
    {
        var station = await _db.CanonicalStations.FindAsync([id], ct);
        if (station is null) return NotFound();

        var autoNameThreshold = _config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90);
        var autoDistThreshold = _config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100);
        var manualNameThreshold = _config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70);
        var manualDistThreshold = _config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300);

        // Collect all raw stops linked to this station (both current and historical)
        var rawStopIds = await _db.RawStops
            .Where(rs => rs.CanonicalStationId == id)
            .Select(rs => rs.Id)
            .Distinct()
            .ToListAsync(ct);

        var candidateRawStopIds = await _db.ReconciliationCandidates
            .Where(rc => rc.SuggestedCanonicalStationId == id)
            .Select(rc => rc.RawStopId)
            .Distinct()
            .ToListAsync(ct);

        var allRawStopIds = rawStopIds.Union(candidateRawStopIds).Distinct().ToList();

        var entries = new List<ReconciliationEntryResponse>();

        if (allRawStopIds.Count > 0)
        {
            // Load all candidates for this station
            var candidates = await _db.ReconciliationCandidates
                .Include(rc => rc.Feed)
                .ThenInclude(f => f.Operator)
                .Include(rc => rc.RawStop)
                .Where(rc => allRawStopIds.Contains(rc.RawStopId))
                .ToListAsync(ct);

            // Load raw stops not covered by candidates (e.g. manually linked without candidate)
            var candidateCoveredIds = candidates.Select(c => c.RawStopId).ToHashSet();
            var extraRawStops = await _db.RawStops
                .Where(rs => allRawStopIds.Contains(rs.Id) && !candidateCoveredIds.Contains(rs.Id))
                .ToListAsync(ct);

            // Load routes for this station (to compute matched/unmatched lines)
            var stationRoutes = await _db.CanonicalRoutes
                .Where(r => _db.StopTimes.Any(st =>
                    st.CanonicalStationId == id
                    && st.Trip.CanonicalRouteId == r.Id))
                .Select(r => new
                {
                    r.ShortName,
                    r.LongName,
                    Display = r.ShortName != null && r.ShortName != "" ? r.ShortName : r.LongName
                })
                .Distinct()
                .ToListAsync(ct);
            var stationLineIds = stationRoutes.Select(r => r.Display).ToHashSet();

            foreach (var candidate in candidates)
            {
                var explanation = ReconciliationManager.ComputeMatchExplanation(
                    candidate.NameSimilarityScore, candidate.DistanceMeters,
                    candidate.NameMatched, candidate.DistanceMatched, candidate.RouteTypeMatched,
                    autoNameThreshold, autoDistThreshold,
                    manualNameThreshold, manualDistThreshold);

                var verdict = ReconciliationManager.ComputeAutoMergeVerdict(
                    candidate.NameSimilarityScore, candidate.DistanceMeters,
                    candidate.NameMatched, candidate.DistanceMatched, candidate.RouteTypeMatched,
                    candidate.RawRouteType.ToString(), candidate.CanonicalRouteType?.ToString(),
                    autoNameThreshold, autoDistThreshold,
                    candidate.Status.ToString());

                // Route/direction check results
                List<string> matchedLines = [];
                List<string> unmatchedLines = [];
                List<string> directionDisagreements = [];

                if (candidate.RawStop is not null)
                {
                    var rawStopRoutes = await _db.CanonicalRoutes
                        .Where(r => _db.StopTimes.Any(st =>
                            st.RawStopEntityId == candidate.RawStop.Id
                            && st.Trip.CanonicalRouteId == r.Id))
                        .Select(r => new
                        {
                            r.ShortName,
                            r.LongName,
                            Display = r.ShortName != null && r.ShortName != "" ? r.ShortName : r.LongName
                        })
                        .Distinct()
                        .ToListAsync(ct);

                    var rawLineIds = rawStopRoutes.Select(r => r.Display).ToHashSet();

                    matchedLines = rawLineIds.Intersect(stationLineIds).OrderBy(x => x).ToList();
                    unmatchedLines = rawLineIds.Except(stationLineIds).OrderBy(x => x).ToList();

                    // Direction check: for shared lines, compare direction sets
                    if (matchedLines.Count > 0)
                    {
                        var rawDirections = await _db.StopTimes
                            .Where(st => st.RawStopEntityId == candidate.RawStop.Id
                                && st.Trip.CanonicalRoute != null
                                && st.Trip.DirectionId.HasValue)
                            .Select(st => new
                            {
                                Line = st.Trip.CanonicalRoute!.ShortName != null && st.Trip.CanonicalRoute!.ShortName != ""
                                    ? st.Trip.CanonicalRoute!.ShortName
                                    : st.Trip.CanonicalRoute!.LongName,
                                st.Trip.DirectionId
                            })
                            .Distinct()
                            .ToListAsync(ct);

                        var stationDirections = await _db.StopTimes
                            .Where(st => st.CanonicalStationId == id
                                && st.Trip.CanonicalRoute != null
                                && st.Trip.DirectionId.HasValue)
                            .Select(st => new
                            {
                                Line = st.Trip.CanonicalRoute!.ShortName != null && st.Trip.CanonicalRoute!.ShortName != ""
                                    ? st.Trip.CanonicalRoute!.ShortName
                                    : st.Trip.CanonicalRoute!.LongName,
                                st.Trip.DirectionId
                            })
                            .Distinct()
                            .ToListAsync(ct);

                        var rawByLine = rawDirections.GroupBy(d => d.Line).ToDictionary(g => g.Key, g => g.Select(d => d.DirectionId!.Value).ToHashSet());
                        var stationByLine = stationDirections.GroupBy(d => d.Line).ToDictionary(g => g.Key, g => g.Select(d => d.DirectionId!.Value).ToHashSet());

                        foreach (var line in matchedLines)
                        {
                            if (!rawByLine.TryGetValue(line, out var rDirs) || !stationByLine.TryGetValue(line, out var sDirs))
                                continue;
                            if (rDirs.Count == 1 && sDirs.Count == 1 && rDirs.Single() != sDirs.Single())
                                directionDisagreements.Add($"{line} (raw: dir {rDirs.Single()}, station: dir {sDirs.Single()})");
                        }
                    }
                }

                entries.Add(new ReconciliationEntryResponse
                {
                    RawStopId = candidate.RawStopId,
                    RawStopName = candidate.RawStopName,
                    RawStopGtfsId = candidate.RawStop?.RawStopId,
                    Status = candidate.Status.ToString(),
                    RawRouteType = candidate.RawRouteType.ToString(),
                    ConfidenceScore = candidate.ConfidenceScore,
                    NameSimilarityScore = candidate.NameSimilarityScore,
                    DistanceMeters = candidate.DistanceMeters,
                    NameMatched = candidate.NameMatched,
                    DistanceMatched = candidate.DistanceMatched,
                    RouteTypeMatched = candidate.RouteTypeMatched,
                    AutoReconciled = candidate.AutoReconciled,
                    MatchExplanation = explanation,
                    AutoMergeVerdict = verdict,
                    OperatorName = candidate.Feed?.Operator?.Name,
                    CreatedAt = candidate.CreatedAt,
                    FeedId = candidate.Feed?.FeedId,
                    MatchedLines = matchedLines.Count > 0 ? matchedLines : null,
                    UnmatchedLines = unmatchedLines.Count > 0 ? unmatchedLines : null,
                    DirectionDisagreements = directionDisagreements.Count > 0 ? directionDisagreements : null
                });
            }

            foreach (var rawStop in extraRawStops)
            {
                entries.Add(new ReconciliationEntryResponse
                {
                    RawStopId = rawStop.Id,
                    RawStopName = rawStop.Name,
                    RawStopGtfsId = rawStop.RawStopId,
                    Status = rawStop.ReconciliationStatus.ToString(),
                    RawRouteType = rawStop.RouteType?.ToString(),
                    AutoReconciled = true,
                    CreatedAt = rawStop.FeedVersion?.FetchedAt ?? DateTime.UtcNow
                });
            }
        }

        entries = entries.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.RawStopName).ToList();

        var result = new StationReconciliationDetailResponse
        {
            StationId = station.Id,
            StationName = station.Name,
            StationGlobalId = station.GlobalId,
            StationOnestopId = station.OnestopId,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            PrimaryRouteType = station.PrimaryRouteType.ToString(),
            Entries = entries
        };

        return Ok(result);
    }
}
