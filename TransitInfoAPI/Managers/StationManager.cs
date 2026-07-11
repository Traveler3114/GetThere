using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Exceptions;

namespace TransitInfoAPI.Managers;

public class StationManager
{
    private readonly TransitDbContext _db;
    private readonly ScheduleManager _schedule;
    private readonly IConfiguration _config;

    public StationManager(TransitDbContext db, ScheduleManager schedule, IConfiguration config) { _db = db; _schedule = schedule; _config = config; }

    public async Task<List<StationResponse>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int page = 1, int perPage = 50, CancellationToken ct = default)
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

        return await query
            .OrderBy(cs => cs.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<object> GetAllGeoJsonAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int limit, CancellationToken ct)
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
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .Where(cs => cs.OnestopId == onestopId && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(StationMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<StationResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .Where(cs => cs.Id == id && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(StationMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<StationResponse>> SearchAsync(string? q, RouteType? routeType, int? countryId, string? countryName, string? stationType, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalStations
            .Include(cs => cs.Country)
            .AsQueryable();

        StationType parsedStationType = default;
        var hasExplicitStationType = !string.IsNullOrWhiteSpace(stationType) && Enum.TryParse<StationType>(stationType, out parsedStationType);
        if (hasExplicitStationType)
            query = query.Where(cs => cs.IsActive && cs.StationType == parsedStationType);
        else
            query = query.Where(cs => cs.IsActive && cs.StationType == StationType.Stop);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(cs => cs.Name.Contains(q));

        if (routeType.HasValue)
            query = query.Where(cs => cs.PrimaryRouteType == routeType.Value);

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(cs =>
                _db.Countries.Any(c => c.Name == countryName && c.Id == cs.CountryId));

        return await query
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

    public async Task<int> GetTotalCountAsync(double? lat, double? lon, double? radiusKm, int? countryId, string? countryName, string? stationType = null, CancellationToken ct = default)
    {
        var query = _db.CanonicalStations.Where(cs => cs.IsActive);

        StationType parsedStationType = default;
        if (!string.IsNullOrWhiteSpace(stationType) && Enum.TryParse<StationType>(stationType, out parsedStationType))
            query = query.Where(cs => cs.StationType == parsedStationType);
        else
            query = query.Where(cs => cs.StationType == StationType.Stop);

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(cs =>
                _db.Countries.Any(c => c.Name == countryName && c.Id == cs.CountryId));

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

        return await query.CountAsync(ct);
    }

    public async Task<StationReconciliationDetailResponse> GetReconciliationDetailAsync(int id, CancellationToken ct)
    {
        var station = await _db.CanonicalStations.FindAsync([id], ct);
        if (station is null) return null!;

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
