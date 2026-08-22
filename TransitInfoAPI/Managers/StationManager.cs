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
    /// Shortest search term that actually filters. Below this the term is ignored — see
    /// <see cref="BuildQuery"/> for why that is better than rejecting it.
    /// </summary>
    internal const int MinimumSearchTermLength = 3;

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

        // Still Contains, deliberately, despite `LIKE '%q%'` being unable to use an index: station
        // names are searched by their middle at least as often as their start — "Glavni" has to find
        // "Zagreb Glavni Kolodvor" — and a prefix match would quietly break the public map's search
        // box to make it fast.
        //
        // The floor is what stops the scan being free to trigger. This endpoint is anonymous and the
        // map calls it per keystroke, so a one- or two-character query used to scan the whole table
        // before the user had typed anything selective. Below the floor the term is ignored rather
        // than rejected: the caller still gets a valid (unfiltered) page, which is what the search
        // box wants while someone is still typing.
        //
        // The durable fix is a full-text index on CanonicalStations.Name and CONTAINS() instead of
        // LIKE. That needs a migration, which could not be generated here — see
        // docs/database-drift.md for the other one owed.
        if (!string.IsNullOrWhiteSpace(q) && q.Trim().Length >= MinimumSearchTermLength)
        {
            var term = q.Trim();
            query = query.Where(cs => cs.Name.Contains(term));
        }

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

    /// <summary>
    /// Feed slugs and operator global ids per station, in two index-covered round trips.
    /// <para>
    /// Kept out of <c>StationMapper.ToResponseExpression</c> on purpose: that expression is shared
    /// by the single-station reads, which have no use for provenance and should not pay for it.
    /// </para>
    /// </summary>
    private async Task<(Dictionary<int, List<string>> Feeds, Dictionary<int, List<string>> Operators)>
        GetProvenanceAsync(List<int> stationIds, CancellationToken ct)
    {
        if (stationIds.Count == 0) return ([], []);

        // RawStops.CanonicalStationId is indexed (TransitDbContext.cs:219). EF 10 parameterises the
        // id list as a JSON array, so 5000 ids is one parameter, not 5000.
        var feedRows = await _db.RawStops.AsNoTracking()
            .Where(rs => rs.CanonicalStationId != null && stationIds.Contains(rs.CanonicalStationId.Value))
            .Select(rs => new { StationId = rs.CanonicalStationId!.Value, Slug = rs.FeedVersion.Feed.FeedId })
            .Distinct()
            .ToListAsync(ct);

        var operatorRows = await _db.Set<CanonicalStationOperator>().AsNoTracking()
            .Where(cso => stationIds.Contains(cso.CanonicalStationId))
            .Select(cso => new { cso.CanonicalStationId, cso.Operator.GlobalId })
            .Distinct()
            .ToListAsync(ct);

        return (
            feedRows.GroupBy(x => x.StationId).ToDictionary(g => g.Key, g => g.Select(x => x.Slug).Order().ToList()),
            operatorRows.GroupBy(x => x.CanonicalStationId).ToDictionary(g => g.Key, g => g.Select(x => x.GlobalId).Order().ToList())
        );
    }

    public async Task<List<StationResponse>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        // No Include here: the query projects through StationMapper.ToResponseExpression, and EF
        // drops an Include on a projecting query — the expression pulls the country columns it needs
        // itself. The Include was silently doing nothing.
        var stations = await BuildQuery(countryId: countryId, lat: lat, lon: lon, radiusKm: radiusKm)
            .OrderBy(cs => cs.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);

        var (feeds, operators) = await GetProvenanceAsync(stations.Select(s => s.Id).ToList(), ct);
        foreach (var s in stations)
        {
            s.FeedIds = feeds.GetValueOrDefault(s.Id) ?? [];
            s.OperatorGlobalIds = operators.GetValueOrDefault(s.Id) ?? [];
        }

        return stations;
    }

    public async Task<object> GetAllGeoJsonAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int limit, CancellationToken ct)
    {
        var allStations = await BuildQuery(countryId: countryId, lat: lat, lon: lon, radiusKm: radiusKm)
            .OrderBy(cs => cs.Id)
            .Take(limit)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);

        var (feeds, operators) = await GetProvenanceAsync(allStations.Select(s => s.Id).ToList(), ct);

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
                ["cityName"] = s.CityName,
                ["feedIds"] = feeds.GetValueOrDefault(s.Id) ?? [],
                ["operatorGlobalIds"] = operators.GetValueOrDefault(s.Id) ?? []
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

            // ── Everything the loop below needs, loaded once ──────────────────────────────────
            //
            // These three reads used to sit *inside* the candidate loop, one or two round trips per
            // iteration against an unbounded candidate list — a station with 200 raw stops cost
            // roughly 400 queries to render one page. Only stationByLine had been hoisted, and only
            // as far as "fill it on first use", which still left the other two per-candidate.
            //
            // Each is the same query the loop issued, widened from one raw stop to all of them and
            // grouped in memory afterwards. The Contains lists are not chunked: EF Core translates
            // them to OPENJSON on SQL Server rather than to a parameter per element, so the old
            // 2100-parameter ceiling does not apply.
            var loopRawStopIds = candidates
                .Where(c => c.RawStop is not null)
                .Select(c => c.RawStop!.Id)
                .Distinct()
                .ToList();

            var linesByRawStop = (await _db.StopTimes
                    .Where(st => st.RawStopEntityId != null
                        && loopRawStopIds.Contains(st.RawStopEntityId.Value)
                        && st.Trip.CanonicalRoute != null)
                    .Select(st => new
                    {
                        RawStopId = st.RawStopEntityId!.Value,
                        Line = st.Trip.CanonicalRoute!.ShortName != null && st.Trip.CanonicalRoute!.ShortName != ""
                            ? st.Trip.CanonicalRoute!.ShortName
                            : st.Trip.CanonicalRoute!.LongName
                    })
                    .Distinct()
                    .ToListAsync(ct))
                .GroupBy(x => x.RawStopId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Line).ToHashSet());

            var directionsByRawStop = (await _db.StopTimes
                    .Where(st => st.RawStopEntityId != null
                        && loopRawStopIds.Contains(st.RawStopEntityId.Value)
                        && st.Trip.CanonicalRoute != null
                        && st.Trip.DirectionId.HasValue)
                    .Select(st => new
                    {
                        RawStopId = st.RawStopEntityId!.Value,
                        Line = st.Trip.CanonicalRoute!.ShortName != null && st.Trip.CanonicalRoute!.ShortName != ""
                            ? st.Trip.CanonicalRoute!.ShortName
                            : st.Trip.CanonicalRoute!.LongName,
                        st.Trip.DirectionId
                    })
                    .Distinct()
                    .ToListAsync(ct))
                .GroupBy(x => x.RawStopId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => x.Line)
                          .ToDictionary(l => l.Key, l => l.Select(x => x.DirectionId!.Value).ToHashSet()));

            // Depends only on the station id, so it returned identical rows on every iteration.
            var stationByLine = (await _db.StopTimes
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
                    // Both lookups are dictionary hits now. A raw stop with no rows in either read
                    // simply has no lines, which is what the queries returned for it before.
                    HashSet<string> rawLineIds = linesByRawStop.TryGetValue(candidate.RawStop.Id, out var lines)
                        ? lines
                        : [];

                    matchedLines = rawLineIds.Intersect(stationLineIds).OrderBy(x => x).ToList();
                    unmatchedLines = rawLineIds.Except(stationLineIds).OrderBy(x => x).ToList();

                    if (matchedLines.Count > 0
                        && directionsByRawStop.TryGetValue(candidate.RawStop.Id, out var rawByLine))
                    {
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
