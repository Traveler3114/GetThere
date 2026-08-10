using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class StationManager
{
    private readonly TransitDbContext _db;
    private readonly ScheduleManager _schedule;
    private readonly IConfiguration _config;

    public StationManager(TransitDbContext db, ScheduleManager schedule, IConfiguration config) { _db = db; _schedule = schedule; _config = config; }

    /// <summary>
    /// The one definition of what filters a station list. Every read below composes it, and so does
    /// <see cref="GetTotalCountAsync"/>.
    /// <para>
    /// It exists because those were four hand-maintained copies of the same predicates, and they had
    /// drifted: <see cref="SearchAsync"/> filtered on <c>q</c> and <c>routeType</c> while the count
    /// beside it had no parameters for either, so every search reported the total number of stations
    /// in the country rather than the number it had matched — a search returning eight rows
    /// advertised tens of thousands, and the map's pager offered hundreds of empty pages.
    /// </para>
    /// </summary>
    private IQueryable<CanonicalStation> BuildQuery(
        string? q = null,
        RouteType? routeType = null,
        int? countryId = null,
        string? countryName = null,
        string? stationType = null,
        double? lat = null,
        double? lon = null,
        double? radiusKm = null)
    {
        var query = _db.CanonicalStations
            .AsNoTracking()
            .Where(cs => cs.IsActive);

        // An unrecognised value falls back to Stop rather than erroring, which is what every caller
        // did before.
        if (!string.IsNullOrWhiteSpace(stationType) && Enum.TryParse<StationType>(stationType, out var parsedStationType))
            query = query.Where(cs => cs.StationType == parsedStationType);
        else
            query = query.Where(cs => cs.StationType == StationType.Stop);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(cs => cs.Name.Contains(q));

        if (routeType.HasValue)
            query = query.Where(cs => cs.PrimaryRouteType == routeType.Value);

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(cs =>
                _db.Countries.Any(c => c.Name == countryName && c.Id == cs.CountryId));

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / GeoConstants.KmPerDegree;

            // Latitude is clamped before the cosine: at the poles cos() reaches zero and the
            // division produces Infinity, which makes the longitude bounds meaningless. No transit
            // stop sits at 90°, but a caller-supplied lat does not have to be sane.
            var clampedLat = Math.Clamp(lat.Value, -89.9, 89.9);
            var lonRange = radiusKm.Value / (GeoConstants.KmPerDegree * Math.Cos(clampedLat * Math.PI / 180));

            query = query.Where(cs =>
                cs.Latitude >= lat.Value - latRange &&
                cs.Latitude <= lat.Value + latRange &&
                cs.Longitude >= lon.Value - lonRange &&
                cs.Longitude <= lon.Value + lonRange);
        }

        return query;
    }

    public async Task<List<StationResponse>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        // No Include here: the query projects through StationMapper.ToResponseExpression, and EF
        // drops an Include on a projecting query — the expression pulls the country columns it needs
        // itself. The Include was silently doing nothing.
        return await BuildQuery(countryId: countryId, lat: lat, lon: lon, radiusKm: radiusKm)
            .OrderBy(cs => cs.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<object> GetAllGeoJsonAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int limit, CancellationToken ct)
    {
        var allStations = await BuildQuery(countryId: countryId, lat: lat, lon: lon, radiusKm: radiusKm)
            .OrderBy(cs => cs.Id)
            .Take(limit)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);

        return GeoJsonGeometry.ToPointCollection(allStations,
            s => s.Latitude, s => s.Longitude,
            s => new Dictionary<string, object?>
            {
                ["id"] = s.Id,
                ["onestopId"] = s.OnestopId,
                ["name"] = s.Name,
                ["stationType"] = s.StationType,
                ["routeType"] = s.PrimaryRouteType,
                ["primaryRouteType"] = s.PrimaryRouteType,
                ["countryName"] = s.CountryName,
                ["cityName"] = s.CityName
            });
    }

    public async Task<StationResponse?> GetByOnestopIdAsync(string onestopId, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .AsNoTracking()
            .Where(cs => cs.OnestopId == onestopId && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(StationMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<StationResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .AsNoTracking()
            .Where(cs => cs.Id == id && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(StationMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<StationResponse>> SearchAsync(string? q, RouteType? routeType, int? countryId, string? countryName, string? stationType, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        return await BuildQuery(q, routeType, countryId, countryName, stationType)
            .OrderBy(cs => cs.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<List<StationOperatorResponse>> GetOperatorsAsync(string onestopId, CancellationToken ct)
    {
        var station = await _db.CanonicalStations
            .FirstOrDefaultAsync(cs => cs.OnestopId == onestopId && cs.IsActive && cs.StationType == StationType.Stop, ct);

        if (station is null) return [];

        return await _db.CanonicalStationOperators
            .Include(cso => cso.Operator)
            .Where(cso => cso.CanonicalStationId == station.Id)
            .Select(cso => StationMapper.ToOperatorResponse(cso))
            .ToListAsync(ct);
    }

    public async Task<List<RouteResponse>> GetRoutesAsync(int stationId, CancellationToken ct)
    {
        var routeIds = await _db.StopTimes
            .Where(st => st.CanonicalStationId == stationId)
            .Where(st => st.Trip.CanonicalRouteId != null)
            .Select(st => st.Trip.CanonicalRouteId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return await _db.CanonicalRoutes
            .Where(r => routeIds.Contains(r.Id))
            .Take(500)
            .Select(RouteMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<List<DepartureResponse>> GetDeparturesAsync(int stationId, DateTime? from, int count, CancellationToken ct)
    {
        return await _schedule.GetDeparturesAsync(stationId, from ?? DateTime.UtcNow, count, ct);
    }

    /// <summary>
    /// Counts what <see cref="SearchAsync"/> and <see cref="GetAllAsync"/> would return. It takes
    /// every filter they do — including <paramref name="q"/> and <paramref name="routeType"/>, which
    /// it previously had no parameters for at all — so a caller cannot pair a filtered page with an
    /// unfiltered total.
    /// </summary>
    public async Task<int> GetTotalCountAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, string? countryName,
        string? stationType = null, string? q = null, RouteType? routeType = null, CancellationToken ct = default)
    {
        return await BuildQuery(q, routeType, countryId, countryName, stationType, lat, lon, radiusKm)
            .CountAsync(ct);
    }

    public async Task<StationReconciliationDetailResponse?> GetReconciliationDetailAsync(int id, CancellationToken ct)
    {
        var station = await _db.CanonicalStations.FindAsync([id], ct);
        if (station is null) return null;

        var autoNameThreshold = _config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90);
        var autoDistThreshold = _config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100);
        var manualNameThreshold = _config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70);
        var manualDistThreshold = _config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300);

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

        List<ReconciliationEntryResponse> entries = [];

        if (allRawStopIds.Count > 0)
        {
            var candidates = await _db.ReconciliationCandidates
                .Include(rc => rc.Feed)
                .ThenInclude(f => f.Operator)
                .Include(rc => rc.RawStop)
                .Where(rc => allRawStopIds.Contains(rc.RawStopId))
                .ToListAsync(ct);

            var candidateCoveredIds = candidates.Select(c => c.RawStopId).ToHashSet();
            var extraRawStops = await _db.RawStops
                .Where(rs => allRawStopIds.Contains(rs.Id) && !candidateCoveredIds.Contains(rs.Id))
                .ToListAsync(ct);

            var canonicalRouteIds = await _db.StopTimes
                .Where(st => st.CanonicalStationId == id && st.Trip.CanonicalRouteId != null)
                .Select(st => st.Trip.CanonicalRouteId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var stationRoutes = await _db.CanonicalRoutes
                .Where(r => canonicalRouteIds.Contains(r.Id))
                .Select(r => new
                {
                    r.ShortName,
                    r.LongName,
                    Display = r.ShortName != null && r.ShortName != "" ? r.ShortName : r.LongName
                })
                .ToListAsync(ct);
            var stationLineIds = stationRoutes.Select(r => r.Display).ToHashSet();

            // Filled on first use inside the loop below and reused for the rest of the request.
            Dictionary<string, HashSet<int>>? stationByLine = null;

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

                        // Loaded once for the whole request rather than per candidate: this query
                        // depends only on the station id, so it returned identical rows on every
                        // iteration of the loop.
                        stationByLine ??= (await _db.StopTimes
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
                                .ToListAsync(ct))
                            .GroupBy(d => d.Line)
                            .ToDictionary(g => g.Key, g => g.Select(d => d.DirectionId!.Value).ToHashSet());

                        var rawByLine = rawDirections.GroupBy(d => d.Line).ToDictionary(g => g.Key, g => g.Select(d => d.DirectionId!.Value).ToHashSet());

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

        return new StationReconciliationDetailResponse
        {
            StationId = station.Id,
            StationName = station.Name,
            StationOnestopId = station.OnestopId,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            PrimaryRouteType = station.PrimaryRouteType.ToString(),
            Entries = entries
        };
    }
}
