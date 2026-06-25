using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Microsoft.Data.SqlClient;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Contracts;
using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Managers;

public class ReconciliationManager
{
    private static readonly Regex KolPattern = new(@"(?<!\w)kol\.(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex UlPattern = new(@"(?<!\w)ul\.(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex StPattern = new(@"(?<!\w)st\.(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex SvPattern = new(@"(?<!\w)sv\.(?=\s|$)", RegexOptions.Compiled);

    private readonly TransitDbContext _db;
    private readonly ILogger<ReconciliationManager> _logger;
    private readonly OnestopIdManager _onestopId;
    private readonly PlaceMatchingManager _placeMatching;
    private readonly IConfiguration _config;

    public ReconciliationManager(
        TransitDbContext db,
        ILogger<ReconciliationManager> logger,
        OnestopIdManager onestopId,
        PlaceMatchingManager placeMatching,
        IConfiguration config)
    {
        _db = db;
        _logger = logger;
        _onestopId = onestopId;
        _placeMatching = placeMatching;
        _config = config;
    }

    public async Task ReconcileFeedVersionAsync(int feedVersionId, CancellationToken ct)
    {
        var rawStops = await _db.RawStops
            .Where(rs => rs.FeedVersionId == feedVersionId && rs.IsActive && rs.StationType == StationType.Stop)
            .ToListAsync(ct);

        if (rawStops.Count == 0) return;

        var feedVersion = await _db.FeedVersions
            .Include(fv => fv.Feed)
            .FirstAsync(fv => fv.Id == feedVersionId, ct);

        var minLat = rawStops.Min(r => r.Lat);
        var maxLat = rawStops.Max(r => r.Lat);
        var minLon = rawStops.Min(r => r.Lon);
        var maxLon = rawStops.Max(r => r.Lon);
        var centerLat = (minLat + maxLat) / 2;
        var latBuffer = 0.001;
        var lonBuffer = latBuffer / Math.Cos(centerLat * Math.PI / 180);
        var existingStations = await _db.CanonicalStations
            .Where(cs => cs.IsActive && cs.StationType == StationType.Stop
                && cs.Latitude >= minLat - latBuffer && cs.Latitude <= maxLat + latBuffer
                && cs.Longitude >= minLon - lonBuffer && cs.Longitude <= maxLon + lonBuffer)
            .ToListAsync(ct);

        // Inactive stations queried by OnestopId only. Fine now — monitor as deactivations accumulate.
        var candidateOnestopIds = rawStops
            .Where(rs => rs.RouteType is not null)
            .Select(rs => _onestopId.GenerateStopOnestopId(rs.Lat, rs.Lon, rs.Name, rs.RouteType!.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inactiveStations = await _db.CanonicalStations
            .Where(cs => !cs.IsActive && candidateOnestopIds.Contains(cs.OnestopId))
            .ToListAsync(ct);
        var inactiveByOnestopId = new Dictionary<string, CanonicalStation>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in inactiveStations)
            inactiveByOnestopId[s.OnestopId] = s;

        var autoNameThreshold = _config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90);
        var autoDistThreshold = _config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100);
        var manualNameThreshold = _config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70);
        var manualDistThreshold = _config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300);
        var feedOperatorId = feedVersion.Feed.OperatorId;

        // Phase 1: build deduplicated station lookup by OnestopId
        var onestopToStation = existingStations.ToDictionary(s => s.OnestopId, s => s);
        var onestopSeen = new HashSet<string>(existingStations.Select(s => s.OnestopId), StringComparer.OrdinalIgnoreCase);
        var newStationList = new List<CanonicalStation>();

        foreach (var rawStop in rawStops)
        {
            if (rawStop.RouteType is null)
                continue;

            var onestopId = _onestopId.GenerateStopOnestopId(rawStop.Lat, rawStop.Lon, rawStop.Name, rawStop.RouteType!.Value);

            if (!onestopSeen.Add(onestopId))
            {
                if (onestopToStation.TryGetValue(onestopId, out var existing))
                    rawStop.CanonicalStationId = existing.Id;
                continue;
            }

            // Re-activate an inactive CanonicalStation with the same OnestopId instead of creating a duplicate
            if (inactiveByOnestopId.TryGetValue(onestopId, out var inactiveStation))
            {
                inactiveStation.IsActive = true;
                inactiveStation.Latitude = rawStop.Lat;
                inactiveStation.Longitude = rawStop.Lon;
                inactiveStation.Name = rawStop.Name;
                inactiveStation.PrimaryRouteType = rawStop.RouteType.Value;
                inactiveStation.StationType = rawStop.StationType;
                rawStop.CanonicalStationId = inactiveStation.Id;
                onestopToStation[onestopId] = inactiveStation;
                newStationList.Add(inactiveStation);
                continue;
            }

            var nearbyMatch = existingStations
                .FirstOrDefault(s => rawStop.RouteType is not null && s.PrimaryRouteType == rawStop.RouteType
                    && CalculateDistanceMeters(rawStop.Lat, rawStop.Lon, s.Latitude, s.Longitude) <= 20
                    && CalculateNameSimilarity(rawStop.Name, s.Name) >= 0.85);

            if (nearbyMatch is not null)
            {
                rawStop.CanonicalStationId = nearbyMatch.Id;
                onestopToStation[onestopId] = nearbyMatch;
                continue;
            }

            var station = new CanonicalStation
            {
                GlobalId = $"gt-{onestopId}",
                OnestopId = onestopId,
                Name = rawStop.Name,
                Latitude = rawStop.Lat,
                Longitude = rawStop.Lon,
                StationType = rawStop.StationType,
                PrimaryRouteType = rawStop.RouteType.Value,
                IsActive = true,
                CountryId = await _placeMatching.DeriveCountryIdAsync(rawStop.Lat, rawStop.Lon, ct),
                CreatedAt = DateTime.UtcNow,
                Geometry = new Point(rawStop.Lon, rawStop.Lat) { SRID = 4326 }
            };
            _db.CanonicalStations.Add(station);
            newStationList.Add(station);
        }

        await _db.SaveChangesAsync(ct);

        foreach (var s in newStationList)
            onestopToStation[s.OnestopId] = s;

        // Set CanonicalStationId for raw stops whose stations were created in this pass
        // so the route/direction lookup below includes them
        foreach (var rawStop in rawStops)
        {
            if (!rawStop.CanonicalStationId.HasValue && rawStop.RouteType is not null)
            {
                var onestopId = _onestopId.GenerateStopOnestopId(
                    rawStop.Lat, rawStop.Lon, rawStop.Name, rawStop.RouteType.Value);
                rawStop.CanonicalStationId = onestopToStation[onestopId].Id;
            }
        }

        existingStations = await _db.CanonicalStations
            .Where(cs => cs.IsActive
                && cs.Latitude >= minLat - latBuffer && cs.Latitude <= maxLat + latBuffer
                && cs.Longitude >= minLon - lonBuffer && cs.Longitude <= maxLon + lonBuffer)
            .ToListAsync(ct);

        // Phase 1.5: build route/direction lookup for route-set and direction matching (Tasks 1.1–1.3)
        var routeLookup = await BuildRouteLookupAsync(feedVersionId, ct);

        var stationToRawStopIds = await _db.RawStops
            .Where(rs => rs.FeedVersionId == feedVersionId && rs.CanonicalStationId.HasValue)
            .GroupBy(rs => rs.CanonicalStationId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Select(rs => rs.RawStopId).Distinct().ToList(), ct);

        // Phase 2: match raw stops to stations and create candidates
        var existingLinkedStationIds = await _db.CanonicalStationOperators
            .Where(cso => cso.OperatorId == feedOperatorId)
            .Select(cso => cso.CanonicalStationId)
            .ToHashSetAsync(ct);

        // Exclude stations already linked to this operator from auto-merge candidates.
        // Within-operator platforms (different tracks/stops at the same station) should
        // keep distinct CanonicalStations. Auto-merge is only for cross-operator dedup,
        // e.g. OBB's "Zagreb Glavni Kolodvor" merging with HZPP's.
        existingStations = existingStations.Where(s => !existingLinkedStationIds.Contains(s.Id)).ToList();

        var addedOperatorLinks = new HashSet<(int CanonicalStationId, int OperatorId)>(
            existingLinkedStationIds.Select(id => (id, feedOperatorId)));

        foreach (var rawStop in rawStops)
        {
            // Stops with null RouteType cannot be matched reliably — mark as Inactive
            if (rawStop.RouteType is null)
            {
                rawStop.ReconciliationStatus = ReconciliationStatus.Inactive;
                continue;
            }

            var onestopId = _onestopId.GenerateStopOnestopId(rawStop.Lat, rawStop.Lon, rawStop.Name, rawStop.RouteType!.Value);
            var station = onestopToStation[onestopId];
            rawStop.CanonicalStationId = station.Id;

            var match = FindBestMatch(
                rawStop.Name, rawStop.Lat, rawStop.Lon, rawStop.RouteType!.Value,
                rawStop.RawStopId, existingStations, autoDistThreshold * 2,
                routeLookup, stationToRawStopIds);

            if (match is not null &&
                match.Value.NameScore >= autoNameThreshold &&
                match.Value.Distance <= autoDistThreshold &&
                match.Value.RouteTypeMatch)
            {
                rawStop.CanonicalStationId = match.Value.Station.Id;
                rawStop.ReconciliationStatus = ReconciliationStatus.AutoMerged;

                _db.ReconciliationCandidates.Add(new ReconciliationCandidate
                {
                    RawStopId = rawStop.Id,
                    RawStopName = rawStop.Name,
                    RawStopLat = rawStop.Lat,
                    RawStopLon = rawStop.Lon,
                    RawRouteType = rawStop.RouteType.Value,
                    CanonicalRouteType = match.Value.Station.PrimaryRouteType,
                    FeedId = feedVersion.FeedId,
                    SuggestedCanonicalStationId = match.Value.Station.Id,
                    ConfidenceScore = (decimal)match.Value.NameScore,
                    NameSimilarityScore = (decimal)match.Value.NameScore,
                    DistanceMeters = (decimal)match.Value.Distance,
                    NameMatched = true,
                    DistanceMatched = true,
                    RouteTypeMatched = true,
                    AutoReconciled = true,
                    Status = ReconciliationStatus.AutoMerged,
                    CreatedAt = DateTime.UtcNow,
                    AutoMergeNameThresholdAtDecision = (decimal)autoNameThreshold,
                    AutoMergeDistanceMetersAtDecision = (decimal)autoDistThreshold,
                    ManualReviewNameThresholdAtDecision = (decimal)manualNameThreshold,
                    ManualReviewDistanceMetersAtDecision = (decimal)manualDistThreshold
                });

                if (addedOperatorLinks.Add((match.Value.Station.Id, feedOperatorId)))
                    _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                    {
                        CanonicalStationId = match.Value.Station.Id,
                        OperatorId = feedOperatorId
                    });
            }
            else if (match is not null &&
                     match.Value.NameScore >= manualNameThreshold &&
                     match.Value.Distance <= manualDistThreshold &&
                     match.Value.RouteTypeMatch)
            {
                _db.ReconciliationCandidates.Add(new ReconciliationCandidate
                {
                    RawStopId = rawStop.Id,
                    RawStopName = rawStop.Name,
                    RawStopLat = rawStop.Lat,
                    RawStopLon = rawStop.Lon,
                    RawRouteType = rawStop.RouteType.Value,
                    CanonicalRouteType = match.Value.Station.PrimaryRouteType,
                    FeedId = feedVersion.FeedId,
                    SuggestedCanonicalStationId = match.Value.Station.Id,
                    ConfidenceScore = (decimal)match.Value.NameScore,
                    NameSimilarityScore = (decimal)match.Value.NameScore,
                    DistanceMeters = (decimal)match.Value.Distance,
                    NameMatched = true,
                    DistanceMatched = true,
                    RouteTypeMatched = true,
                    AutoReconciled = false,
                    Status = ReconciliationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    AutoMergeNameThresholdAtDecision = (decimal)autoNameThreshold,
                    AutoMergeDistanceMetersAtDecision = (decimal)autoDistThreshold,
                    ManualReviewNameThresholdAtDecision = (decimal)manualNameThreshold,
                    ManualReviewDistanceMetersAtDecision = (decimal)manualDistThreshold
                });

                if (addedOperatorLinks.Add((match.Value.Station.Id, feedOperatorId)))
                    _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                    {
                        CanonicalStationId = match.Value.Station.Id,
                        OperatorId = feedOperatorId
                    });
            }
            else
            {
                rawStop.CanonicalStationId = station.Id;

                var hasExistingLinkedStops = stationToRawStopIds.TryGetValue(station.Id, out var linkedIds) && linkedIds.Count > 0;
                var routeMismatch = hasExistingLinkedStops && !HasRouteOverlap(rawStop.RawStopId, station.Id, routeLookup, stationToRawStopIds);
                var dirMismatch = hasExistingLinkedStops && !routeMismatch && HasDirectionMismatch(rawStop.RawStopId, station.Id, routeLookup, stationToRawStopIds);

                if (routeMismatch || dirMismatch)
                {
                    rawStop.ReconciliationStatus = ReconciliationStatus.AutoMerged;

                    var reason = routeMismatch ? "RouteSetMismatch" : "DirectionMismatch";
                    var detail = routeMismatch
                        ? BuildRouteSetMismatchDetail(rawStop.RawStopId, station.Id, routeLookup, stationToRawStopIds)
                        : BuildDirectionMismatchDetail(rawStop.RawStopId, station.Id, routeLookup, stationToRawStopIds);

                    _db.StationSplitLogs.Add(new StationSplitLog
                    {
                        RawStopId = rawStop.Id,
                        FeedVersionId = feedVersionId,
                        CandidateStationId = station.Id,
                        Reason = reason,
                        Detail = detail,
                        CreatedAt = DateTime.UtcNow
                    });

                    if (addedOperatorLinks.Add((station.Id, feedOperatorId)))
                        _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                        {
                            CanonicalStationId = station.Id,
                            OperatorId = feedOperatorId
                        });
                }
                else
                {
                    rawStop.ReconciliationStatus = ReconciliationStatus.NewStation;

                    if (addedOperatorLinks.Add((station.Id, feedOperatorId)))
                        _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                        {
                            CanonicalStationId = station.Id,
                            OperatorId = feedOperatorId
                        });
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Reconciled {Count} raw stops for FeedVersion {FeedVersionId}", rawStops.Count, feedVersionId);
    }

    private async Task<Dictionary<string, List<(string LineIdentity, int? DirectionId)>>> BuildRouteLookupAsync(
        int feedVersionId, CancellationToken ct)
    {
        var stopRoutes = await _db.StopTimes
            .Where(st => st.Trip.FeedVersionId == feedVersionId && st.RawStopId != string.Empty)
            .Select(st => new
            {
                st.RawStopId,
                st.Trip.DirectionId,
                ShortName = st.Trip.CanonicalRoute != null ? st.Trip.CanonicalRoute.ShortName : null,
                LongName = st.Trip.CanonicalRoute != null ? st.Trip.CanonicalRoute.LongName : null,
                st.Trip.RouteId
            })
            .ToListAsync(ct);

        var lookup = new Dictionary<string, List<(string LineIdentity, int? DirectionId)>>();
        foreach (var row in stopRoutes)
        {
            var line = !string.IsNullOrEmpty(row.ShortName) ? row.ShortName
                : !string.IsNullOrEmpty(row.LongName) ? row.LongName
                : row.RouteId;

            if (!lookup.TryGetValue(row.RawStopId, out var list))
            {
                list = [];
                lookup[row.RawStopId] = list;
            }

            var pair = (LineIdentity: line, DirectionId: row.DirectionId);
            if (!list.Any(p => p.LineIdentity == pair.LineIdentity && p.DirectionId == pair.DirectionId))
                list.Add(pair);
        }

        return lookup;
    }

    public async Task<CanonicalStation> CreateCanonicalStationAsync(RawStop rawStop, CancellationToken ct)
    {
        var routeType = rawStop.RouteType ?? RouteType.Bus;
        var onestopId = _onestopId.GenerateStopOnestopId(rawStop.Lat, rawStop.Lon, rawStop.Name, routeType);

        var station = new CanonicalStation
        {
            GlobalId = $"gt-{onestopId}",
            OnestopId = onestopId,
            Name = rawStop.Name,
            Latitude = rawStop.Lat,
            Longitude = rawStop.Lon,
            StationType = rawStop.StationType,
            PrimaryRouteType = routeType,
            IsActive = true,
            CountryId = await _placeMatching.DeriveCountryIdAsync(rawStop.Lat, rawStop.Lon, ct),
            CreatedAt = DateTime.UtcNow,
            Geometry = new Point(rawStop.Lon, rawStop.Lat) { SRID = 4326 }
        };

        _db.CanonicalStations.Add(station);
        await _db.SaveChangesAsync(ct);
        return station;
    }

    public async Task LinkOperatorToStationAsync(int canonicalStationId, int operatorId, CancellationToken ct)
    {
        var exists = await _db.CanonicalStationOperators
            .AnyAsync(cso => cso.CanonicalStationId == canonicalStationId && cso.OperatorId == operatorId, ct);

        if (!exists)
        {
            _db.CanonicalStationOperators.Add(new CanonicalStationOperator
            {
                CanonicalStationId = canonicalStationId,
                OperatorId = operatorId
            });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Unique constraint violation — link already exists, treat as success
            }
        }
    }

    public async Task ApproveCandidateAsync(int id, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var candidate = await _db.ReconciliationCandidates
            .Include(c => c.Feed)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null)
            throw new AppException("Candidate not found", 404);

        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        if (candidate.SuggestedCanonicalStationId.HasValue)
        {
            var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
            if (rawStop is not null)
            {
                if (rawStop.CanonicalStationId.HasValue
                    && rawStop.CanonicalStationId != candidate.SuggestedCanonicalStationId)
                    throw new AppException(
                        $"Raw stop was reassigned to station {rawStop.CanonicalStationId} after this candidate was created. Review and re-confirm.", 409);

                rawStop.CanonicalStationId = candidate.SuggestedCanonicalStationId;
                rawStop.ReconciliationStatus = ReconciliationStatus.ManuallyApproved;
            }

            await LinkOperatorToStationAsync(candidate.SuggestedCanonicalStationId.Value, candidate.Feed.OperatorId, ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RejectCandidateAsync(int id, bool createNewStation = false, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var candidate = await _db.ReconciliationCandidates
            .Include(c => c.Feed)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null)
            throw new AppException("Candidate not found", 404);

        if (createNewStation)
        {
            var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
            if (rawStop is not null)
            {
                var newStation = await CreateCanonicalStationAsync(rawStop, ct);
                candidate.SuggestedCanonicalStationId = newStation.Id;
                rawStop.CanonicalStationId = newStation.Id;
                rawStop.ReconciliationStatus = ReconciliationStatus.NewStation;
            }
        }
        else
        {
            var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
            if (rawStop is not null)
            {
                rawStop.CanonicalStationId = null;
                rawStop.ReconciliationStatus = ReconciliationStatus.Rejected;
            }
        }

        candidate.Status = ReconciliationStatus.Rejected;
        candidate.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string?> ReassignCandidateAsync(int id, int canonicalStationId, bool force = false, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync([id], ct);
        if (candidate is null)
            throw new AppException("Candidate not found", 404);

        var station = await _db.CanonicalStations.FindAsync([canonicalStationId], ct);
        if (station is null)
            throw new AppException("Station not found", 404);

        if (!station.IsActive)
            throw new AppException("Cannot reassign to an inactive station", 400);

        var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
        if (rawStop is not null && rawStop.RouteType is not null && rawStop.RouteType.Value != station.PrimaryRouteType)
        {
            if (!force)
                throw new AppException(
                    $"Cannot reassign: route type mismatch ({rawStop.RouteType.Value} vs {station.PrimaryRouteType}). Use force=true to override.", 400);
            _logger.LogWarning(
                "Reassigning raw stop {RawStopId} (RouteType={RawType}) to station {StationId} (PrimaryRouteType={StationType}) with mismatched route type (forced)",
                candidate.RawStopId, rawStop.RouteType.Value, station.Id, station.PrimaryRouteType);
        }

        var warning = candidate.SuggestedCanonicalStationId.HasValue
            ? await CheckManualActionWarningAsync(candidate.SuggestedCanonicalStationId.Value, canonicalStationId, ct)
            : null;

        candidate.SuggestedCanonicalStationId = canonicalStationId;
        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        if (rawStop is not null)
        {
            rawStop.CanonicalStationId = canonicalStationId;
            rawStop.ReconciliationStatus = ReconciliationStatus.ManuallyApproved;
        }

        await _db.SaveChangesAsync(ct);
        return warning;
    }

    public async Task<string?> CheckManualActionWarningAsync(int stationAId, int stationBId, CancellationToken ct)
    {
        if (stationAId == stationBId) return null;

        var stationA = await _db.CanonicalStations.FindAsync([stationAId], ct);
        var stationB = await _db.CanonicalStations.FindAsync([stationBId], ct);
        if (stationA is null || stationB is null) return null;

        var linesA = new HashSet<string>();
        var linesB = new HashSet<string>();
        var dirsA = new Dictionary<string, HashSet<int?>>();
        var dirsB = new Dictionary<string, HashSet<int?>>();

        await LoadStationRouteDataAsync(stationAId, linesA, dirsA, ct);
        await LoadStationRouteDataAsync(stationBId, linesB, dirsB, ct);

        if (linesA.Count == 0 || linesB.Count == 0)
            return null;

        if (!HasRouteSetOverlap(linesA, linesB))
        {
            var aList = string.Join(", ", linesA.OrderBy(x => x));
            var bList = string.Join(", ", linesB.OrderBy(x => x));
            return $"Route-set mismatch: station {stationAId} has lines [{aList}], station {stationBId} has lines [{bList}], no overlap.";
        }

        if (DirectionSetsConflict(dirsA, dirsB))
        {
            var shared = dirsA.Keys.Intersect(dirsB.Keys).OrderBy(x => x);
            var details = new List<string>();
            foreach (var line in shared)
            {
                var aStr = string.Join(",", dirsA[line].Select(d => d?.ToString() ?? "null"));
                var bStr = string.Join(",", dirsB[line].Select(d => d?.ToString() ?? "null"));
                details.Add($"{line} (A: [{aStr}], B: [{bStr}])");
            }
            return $"Direction mismatch on shared lines: {string.Join("; ", details)}.";
        }

        return null;
    }

    private async Task LoadStationRouteDataAsync(
        int stationId, HashSet<string> lines,
        Dictionary<string, HashSet<int?>> dirs, CancellationToken ct)
    {
        var rawStopIds = await _db.RawStops
            .Where(rs => rs.CanonicalStationId == stationId)
            .Select(rs => rs.RawStopId)
            .Distinct()
            .ToListAsync(ct);

        if (rawStopIds.Count == 0) return;

        var stopRoutes = await _db.StopTimes
            .Where(st => rawStopIds.Contains(st.RawStopId) && st.Trip.CanonicalRoute != null)
            .Select(st => new
            {
                st.RawStopId,
                st.Trip.DirectionId,
                Line = st.Trip.CanonicalRoute!.ShortName != null && st.Trip.CanonicalRoute!.ShortName != ""
                    ? st.Trip.CanonicalRoute!.ShortName
                    : st.Trip.CanonicalRoute!.LongName
            })
            .Distinct()
            .ToListAsync(ct);

        foreach (var row in stopRoutes)
        {
            lines.Add(row.Line);
            if (!dirs.TryGetValue(row.Line, out var lineDirs))
            {
                lineDirs = [];
                dirs[row.Line] = lineDirs;
            }
            lineDirs.Add(row.DirectionId);
        }
    }

    public async Task MergeStationsAsync(int sourceStationId, int targetStationId, CancellationToken ct)
    {
        if (sourceStationId == targetStationId)
            throw new AppException("Cannot merge a station with itself", 400);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var source = await _db.CanonicalStations.FindAsync([sourceStationId], ct);
        var target = await _db.CanonicalStations.FindAsync([targetStationId], ct);

        if (source is null)
            throw new AppException("Source station not found", 404);
        if (target is null)
            throw new AppException("Target station not found", 404);
        if (!source.IsActive)
            throw new AppException("Source station is already inactive", 400);
        if (!target.IsActive)
            throw new AppException("Target station is inactive", 400);
        if (source.PrimaryRouteType != target.PrimaryRouteType)
            throw new AppException(
                $"Cannot merge stations with different primary route types ({source.PrimaryRouteType} vs {target.PrimaryRouteType})", 400);

        // Check route-set conflicts
        var linesSrc = new HashSet<string>();
        var linesTgt = new HashSet<string>();
        var dirsSrc = new Dictionary<string, HashSet<int?>>();
        var dirsTgt = new Dictionary<string, HashSet<int?>>();
        await LoadStationRouteDataAsync(sourceStationId, linesSrc, dirsSrc, ct);
        await LoadStationRouteDataAsync(targetStationId, linesTgt, dirsTgt, ct);
        if (linesSrc.Count > 0 && linesTgt.Count > 0 && !HasRouteSetOverlap(linesSrc, linesTgt))
        {
            var srcList = string.Join(", ", linesSrc.OrderBy(x => x));
            var tgtList = string.Join(", ", linesTgt.OrderBy(x => x));
            _logger.LogWarning(
                "Merging stations with non-overlapping route sets: source has [{Src}] and target has [{Tgt}]. Proceeding because stations may be served by different operators.",
                srcList, tgtList);
        }
        if (linesSrc.Count > 0 && linesTgt.Count > 0 && HasRouteSetOverlap(linesSrc, linesTgt) && DirectionSetsConflict(dirsSrc, dirsTgt))
        {
            var shared = dirsSrc.Keys.Intersect(dirsTgt.Keys).OrderBy(x => x);
            var details = new List<string>();
            foreach (var line in shared)
            {
                var aStr = string.Join(",", dirsSrc[line].Select(d => d?.ToString() ?? "null"));
                var bStr = string.Join(",", dirsTgt[line].Select(d => d?.ToString() ?? "null"));
                details.Add($"{line} (src: [{aStr}], tgt: [{bStr}])");
            }
            throw new AppException(
                $"Direction mismatch on shared lines: {string.Join("; ", details)}. Merge would produce contradictory route data.", 400);
        }

        // Reassign raw stops from source to target
        var rawStops = await _db.RawStops
            .Where(rs => rs.CanonicalStationId == sourceStationId)
            .ToListAsync(ct);
        foreach (var rs in rawStops)
        {
            rs.CanonicalStationId = targetStationId;
            rs.ReconciliationStatus = ReconciliationStatus.ManuallyApproved;
        }

        // Carry over operator links (dedup)
        var sourceOps = await _db.CanonicalStationOperators
            .Where(cso => cso.CanonicalStationId == sourceStationId)
            .ToListAsync(ct);
        var targetOpIds = await _db.CanonicalStationOperators
            .Where(cso => cso.CanonicalStationId == targetStationId)
            .Select(cso => cso.OperatorId)
            .ToHashSetAsync(ct);
        var operatorsMerged = 0;
        foreach (var link in sourceOps)
        {
            if (!targetOpIds.Contains(link.OperatorId))
            {
                _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                {
                    CanonicalStationId = targetStationId,
                    OperatorId = link.OperatorId
                });
                operatorsMerged++;
            }
        }

        // Update reconciliation candidates pointing to source
        var candidates = await _db.ReconciliationCandidates
            .Where(rc => rc.SuggestedCanonicalStationId == sourceStationId)
            .ToListAsync(ct);
        foreach (var c in candidates)
            c.SuggestedCanonicalStationId = targetStationId;

        // Deactivate source
        source.IsActive = false;

        var mergeLog = new StationMergeLog
        {
            SourceStationId = sourceStationId,
            SourceStationGlobalId = source.GlobalId,
            TargetStationId = targetStationId,
            RawStopsMovedCount = rawStops.Count,
            MovedRawStopIds = "[" + string.Join(",", rawStops.Select(rs => rs.Id)) + "]",
            MergedAt = DateTime.UtcNow
        };
        mergeLog.MovedRawStops = rawStops.Select(rs => new StationMergeMovedRawStop { RawStopId = rs.Id }).ToList();
        _db.StationMergeLogs.Add(mergeLog);

        await _db.SaveChangesAsync(ct);

        // Update StopTimes.CanonicalStationId for moved raw stops
        // so schedule queries find departures from both operators
        var rawStopIds = rawStops.Select(rs => rs.Id).ToList();
        await _db.StopTimes
            .Where(st => st.RawStopEntityId.HasValue && rawStopIds.Contains(st.RawStopEntityId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(st => st.CanonicalStationId, targetStationId), ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Merged station {SourceId} into {TargetId}: {RawCount} raw stops moved, {OpCount} operators merged",
            sourceStationId, targetStationId, rawStops.Count, operatorsMerged);
    }

    public async Task UnmergeStationsAsync(int mergeLogId, CancellationToken ct)
    {
        var log = await _db.StationMergeLogs
            .Include(ml => ml.MovedRawStops)
            .FirstOrDefaultAsync(ml => ml.Id == mergeLogId, ct);
        if (log is null)
            throw new AppException("Merge log entry not found", 404);

        var source = await _db.CanonicalStations.FindAsync([log.SourceStationId], ct);
        if (source is null)
            throw new AppException("Source station not found", 404);
        if (source.IsActive)
            throw new AppException("Source station is already active — unmerge was already performed or never needed", 400);

        var target = await _db.CanonicalStations.FindAsync([log.TargetStationId], ct);
        if (target is null)
            throw new AppException("Target station not found", 404);

        // Reactivate source station
        source.IsActive = true;

        // Read moved raw stops from join table (fall back to CSV parse for legacy records)
        var rawStopIds = log.MovedRawStops.Any()
            ? log.MovedRawStops.Select(mrs => mrs.RawStopId).ToList()
            : (log.MovedRawStopIds ?? "[]")
                .Trim('[', ']')
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

        if (rawStopIds.Count == 0)
            throw new AppException("No raw stop IDs recorded in merge log — cannot unmerge", 400);

        var movedRawStops = await _db.RawStops
            .Where(rs => rawStopIds.Contains(rs.Id))
            .ToListAsync(ct);

        foreach (var rs in movedRawStops)
        {
            rs.CanonicalStationId = log.SourceStationId;
            rs.ReconciliationStatus = ReconciliationStatus.Pending;
        }

        // Update StopTimes CanonicalStationId back to source
        if (rawStopIds.Count > 0)
        {
            await _db.StopTimes
                .Where(st => st.RawStopEntityId.HasValue && rawStopIds.Contains(st.RawStopEntityId.Value))
                .ExecuteUpdateAsync(setters => setters.SetProperty(st => st.CanonicalStationId, log.SourceStationId), ct);
        }

        // Move reconciliation candidates back to source
        var candidates = await _db.ReconciliationCandidates
            .Where(rc => rc.SuggestedCanonicalStationId == log.TargetStationId
                && rawStopIds.Contains(rc.RawStopId))
            .ToListAsync(ct);
        foreach (var c in candidates)
            c.SuggestedCanonicalStationId = log.SourceStationId;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Unmerged station {SourceId} from {TargetId}: {RawCount} raw stops returned",
            log.SourceStationId, log.TargetStationId, movedRawStops.Count);
    }

    public async Task<MergePreviewResponse> GetMergePreviewAsync(int stationAId, int stationBId, CancellationToken ct)
    {
        var a = await _db.CanonicalStations.FindAsync([stationAId], ct);
        var b = await _db.CanonicalStations.FindAsync([stationBId], ct);

        if (a is null || b is null)
            throw new AppException("One or both stations not found", 404);

        var aOpCount = await _db.CanonicalStationOperators
            .CountAsync(cso => cso.CanonicalStationId == stationAId, ct);
        var bOpCount = await _db.CanonicalStationOperators
            .CountAsync(cso => cso.CanonicalStationId == stationBId, ct);

        var aRsCount = await _db.RawStops
            .CountAsync(rs => rs.CanonicalStationId == stationAId, ct);
        var bRsCount = await _db.RawStops
            .CountAsync(rs => rs.CanonicalStationId == stationBId, ct);

        string? warning = null;
        if (a.PrimaryRouteType != b.PrimaryRouteType)
            warning = $"Route type mismatch: {a.PrimaryRouteType} vs {b.PrimaryRouteType}";

        return new MergePreviewResponse
        {
            StationA = new MergePreviewStation
            {
                Id = a.Id, Name = a.Name, PrimaryRouteType = a.PrimaryRouteType.ToString(),
                IsActive = a.IsActive, OperatorCount = aOpCount, RawStopCount = aRsCount
            },
            StationB = new MergePreviewStation
            {
                Id = b.Id, Name = b.Name, PrimaryRouteType = b.PrimaryRouteType.ToString(),
                IsActive = b.IsActive, OperatorCount = bOpCount, RawStopCount = bRsCount
            },
            CanMerge = a.IsActive && b.IsActive && a.PrimaryRouteType == b.PrimaryRouteType,
            Warning = warning
        };
    }

    private (CanonicalStation Station, double NameScore, double Distance, bool RouteTypeMatch)? FindBestMatch(
        string rawName, double rawLat, double rawLon, RouteType rawRouteType,
        string rawStopId, List<CanonicalStation> stations, double searchRadiusMeters,
        Dictionary<string, List<(string LineIdentity, int? DirectionId)>> routeLookup,
        Dictionary<int, List<string>> stationToRawStopIds)
    {
        (CanonicalStation Station, double NameScore, double Distance, bool RouteTypeMatch)? best = null;

        foreach (var station in stations.Where(s => s.PrimaryRouteType == rawRouteType))
        {
            var dist = CalculateDistanceMeters(rawLat, rawLon, station.Latitude, station.Longitude);
            if (dist > searchRadiusMeters) continue;

            var nameScore = CalculateNameSimilarity(rawName, station.Name);
            if (nameScore < 0.3) continue;

            // Route-set check (Task 1.2) — skip candidate if no route overlap
            if (!HasRouteOverlap(rawStopId, station.Id, routeLookup, stationToRawStopIds))
                continue;

            // Direction check (Task 1.3) — skip candidate if direction mismatch on any shared line
            if (HasDirectionMismatch(rawStopId, station.Id, routeLookup, stationToRawStopIds))
                continue;

            if (best is null || nameScore > best.Value.NameScore)
            {
                best = (station, nameScore, dist, true);
            }
        }

        return best;
    }

    private static bool HasRouteOverlap(
        string rawStopId, int stationId,
        Dictionary<string, List<(string LineIdentity, int? DirectionId)>> routeLookup,
        Dictionary<int, List<string>> stationToRawStopIds)
    {
        if (!routeLookup.TryGetValue(rawStopId, out var incomingRoutes))
            return false;

        if (!stationToRawStopIds.TryGetValue(stationId, out var linkedRawStopIds))
            return false;

        var stationLineIds = new HashSet<string>();
        foreach (var linkedId in linkedRawStopIds)
        {
            if (routeLookup.TryGetValue(linkedId, out var linkedRoutes))
            {
                foreach (var (line, _) in linkedRoutes)
                    stationLineIds.Add(line);
            }
        }

        if (stationLineIds.Count == 0)
            return false;

        var incomingLineIds = new HashSet<string>();
        foreach (var (line, _) in incomingRoutes)
            incomingLineIds.Add(line);

        return HasRouteSetOverlap(incomingLineIds, stationLineIds);
    }

    private static bool HasRouteSetOverlap(HashSet<string> linesA, HashSet<string> linesB)
    {
        return linesA.Overlaps(linesB);
    }

    private static bool HasDirectionMismatch(
        string rawStopId, int stationId,
        Dictionary<string, List<(string LineIdentity, int? DirectionId)>> routeLookup,
        Dictionary<int, List<string>> stationToRawStopIds)
    {
        var incomingRoutes = routeLookup[rawStopId];
        var linkedRawStopIds = stationToRawStopIds[stationId];

        var incomingByLine = GroupByLineIdentity(incomingRoutes);
        var stationByLine = new Dictionary<string, HashSet<int?>>();
        foreach (var linkedId in linkedRawStopIds)
        {
            if (routeLookup.TryGetValue(linkedId, out var linkedRoutes))
            {
                foreach (var (line, dir) in linkedRoutes)
                {
                    if (!stationByLine.TryGetValue(line, out var dirs))
                    {
                        dirs = [];
                        stationByLine[line] = dirs;
                    }
                    dirs.Add(dir);
                }
            }
        }

        return DirectionSetsConflict(incomingByLine, stationByLine);
    }

    private static bool DirectionSetsConflict(
        Dictionary<string, HashSet<int?>> dirsA,
        Dictionary<string, HashSet<int?>> dirsB)
    {
        var sharedLines = new HashSet<string>();
        foreach (var line in dirsA.Keys)
        {
            if (dirsB.ContainsKey(line))
                sharedLines.Add(line);
        }

        foreach (var line in sharedLines)
        {
            var aDirs = dirsA[line];
            var bDirs = dirsB[line];

            if (aDirs.Count == 0 || bDirs.Count == 0)
                return true;

            if (aDirs.Any(d => d is null) || bDirs.Any(d => d is null))
                return true;

            if (aDirs.Contains(0) && aDirs.Contains(1))
                continue;

            if (bDirs.Contains(0) && bDirs.Contains(1))
                continue;

            if (aDirs.Count == 1 && bDirs.Count == 1)
            {
                var aDir = aDirs.Single()!.Value;
                var bDir = bDirs.Single()!.Value;
                if (aDir != bDir)
                    return true;
            }
        }

        return false;
    }

    private static Dictionary<string, HashSet<int?>> GroupByLineIdentity(
        List<(string LineIdentity, int? DirectionId)> routes)
    {
        var result = new Dictionary<string, HashSet<int?>>();
        foreach (var (line, dir) in routes)
        {
            if (!result.TryGetValue(line, out var dirs))
            {
                dirs = [];
                result[line] = dirs;
            }
            dirs.Add(dir);
        }
        return result;
    }

    private static string BuildRouteSetMismatchDetail(
        string rawStopId, int stationId,
        Dictionary<string, List<(string LineIdentity, int? DirectionId)>> routeLookup,
        Dictionary<int, List<string>> stationToRawStopIds)
    {
        if (!routeLookup.TryGetValue(rawStopId, out var incomingRoutes))
            return "No incoming routes";

        var incomingLines = incomingRoutes.Select(r => r.LineIdentity).Distinct().OrderBy(x => x).ToList();

        var stationLines = new HashSet<string>();
        if (stationToRawStopIds.TryGetValue(stationId, out var linkedIds))
        {
            foreach (var linkedId in linkedIds)
            {
                if (routeLookup.TryGetValue(linkedId, out var linkedRoutes))
                {
                    foreach (var (line, _) in linkedRoutes)
                        stationLines.Add(line);
                }
            }
        }

        var incomingOnly = incomingLines.Except(stationLines).OrderBy(x => x).ToList();
        var stationOnly = stationLines.Except(incomingLines).OrderBy(x => x).ToList();

        var parts = new List<string>();
        if (incomingOnly.Count > 0)
            parts.Add($"raw-only lines: [{string.Join(", ", incomingOnly)}]");
        if (stationOnly.Count > 0)
            parts.Add($"station-only lines: [{string.Join(", ", stationOnly)}]");

        return string.Join("; ", parts);
    }

    private static string BuildDirectionMismatchDetail(
        string rawStopId, int stationId,
        Dictionary<string, List<(string LineIdentity, int? DirectionId)>> routeLookup,
        Dictionary<int, List<string>> stationToRawStopIds)
    {
        var incomingRoutes = routeLookup[rawStopId];
        var linkedRawStopIds = stationToRawStopIds[stationId];

        var incomingByLine = GroupByLineIdentity(incomingRoutes);
        var stationByLine = new Dictionary<string, HashSet<int?>>();
        foreach (var linkedId in linkedRawStopIds)
        {
            if (routeLookup.TryGetValue(linkedId, out var linkedRoutes))
            {
                foreach (var (line, dir) in linkedRoutes)
                {
                    if (!stationByLine.TryGetValue(line, out var dirs))
                    {
                        dirs = [];
                        stationByLine[line] = dirs;
                    }
                    dirs.Add(dir);
                }
            }
        }

        var conflicts = new List<string>();
        foreach (var line in incomingByLine.Keys.Intersect(stationByLine.Keys).OrderBy(x => x))
        {
            var inDirs = incomingByLine[line];
            var stDirs = stationByLine[line];

            if (inDirs.Count == 1 && stDirs.Count == 1)
            {
                var inDir = inDirs.Single();
                var stDir = stDirs.Single();
                if (inDir != stDir)
                    conflicts.Add($"{line} (raw: dir {inDir?.ToString() ?? "null"}, station: dir {stDir?.ToString() ?? "null"})");
            }
            else if (inDirs.Count == 0 || stDirs.Count == 0 || inDirs.Any(d => d is null) || stDirs.Any(d => d is null))
            {
                var inStr = string.Join(",", inDirs.Select(d => d?.ToString() ?? "null"));
                var stStr = string.Join(",", stDirs.Select(d => d?.ToString() ?? "null"));
                conflicts.Add($"{line} (raw: [{inStr}], station: [{stStr}])");
            }
        }

        return conflicts.Count > 0 ? string.Join("; ", conflicts) : "Direction mismatch detected";
    }

    public double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        return GeoUtils.CalculateDistanceMeters(lat1, lon1, lat2, lon2);
    }

    public double CalculateNameSimilarity(string name1, string name2)
    {
        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return 0;

        var n1 = NormalizeName(name1);
        var n2 = NormalizeName(name2);

        if (n1 == n2) return 1.0;
        if (n1.Length >= 5 && n2.Length >= 5 && (n1.Contains(n2) || n2.Contains(n1))) return 0.85;

        var dist = LevenshteinDistance(n1, n2);
        var maxLen = Math.Max(n1.Length, n2.Length);
        if (maxLen == 0) return 0;

        return 1.0 - (double)dist / maxLen;
    }

    internal static string NormalizeName(string name)
    {
        var lower = name.ToLowerInvariant().Trim();

        lower = KolPattern.Replace(lower, "kolodvor");
        lower = UlPattern.Replace(lower, "ulica");
        lower = StPattern.Replace(lower, "sveti");
        lower = SvPattern.Replace(lower, "sveti");

        var sb = new System.Text.StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            var mapped = c switch
            {
                'č' or 'ć' => 'c',
                'š' => 's',
                'ž' => 'z',
                'đ' => 'd',
                'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => 'a',
                'è' or 'é' or 'ê' or 'ë' => 'e',
                'ì' or 'í' or 'î' or 'ï' => 'i',
                'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' => 'o',
                'ù' or 'ú' or 'û' or 'ü' => 'u',
                'ñ' => 'n',
                _ => c
            };
            if (char.IsLetterOrDigit(mapped) || mapped == ' ')
                sb.Append(mapped);
        }

        var result = sb.ToString().Trim();
        while (result.Contains("  "))
            result = result.Replace("  ", " ");

        return result;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var m = s.Length;
        var n = t.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) d[i, 0] = i;
        for (var j = 0; j <= n; j++) d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }

    internal static string ComputeMatchExplanation(
        decimal nameSimilarity, decimal distanceMeters,
        bool nameMatched, bool distanceMatched, bool routeTypeMatched,
        double autoNameThreshold, double autoDistThreshold,
        double manualNameThreshold, double manualDistThreshold)
    {
        var parts = new List<string>();
        var namePct = (nameSimilarity * 100).ToString("F0");
        var distStr = distanceMeters < 1000
            ? distanceMeters.ToString("F0") + " m"
            : (distanceMeters / 1000).ToString("F2") + " km";

        parts.Add($"Name: {namePct}% match");
        if (nameSimilarity >= (decimal)autoNameThreshold)
            parts.Add($"(≥{autoNameThreshold * 100:F0}% auto-merge threshold ✓)");
        else if (nameSimilarity >= (decimal)manualNameThreshold)
            parts.Add($"(≥{manualNameThreshold * 100:F0}% manual-review threshold ✓, <{autoNameThreshold * 100:F0}% auto-merge)");
        else
            parts.Add($"(<{manualNameThreshold * 100:F0}% manual-review threshold ❌)");

        parts.Add($"Distance: {distStr}");
        if (distanceMeters <= (decimal)autoDistThreshold)
            parts.Add($"(≤{autoDistThreshold:F0}m auto-merge threshold ✓)");
        else if (distanceMeters <= (decimal)manualDistThreshold)
            parts.Add($"(≤{manualDistThreshold:F0}m manual-review threshold ✓, >{autoDistThreshold:F0}m auto-merge)");
        else
            parts.Add($"(>{manualDistThreshold:F0}m manual-review threshold ❌)");

        parts.Add(routeTypeMatched ? "Route type: Match ✓" : "Route type: Mismatch ❌");

        parts.Add($"Overall: {(nameMatched && distanceMatched && routeTypeMatched ? "All criteria met" : "Some criteria not met")}");

        return string.Join(" | ", parts);
    }

    internal static string ComputeAutoMergeVerdict(
        decimal nameSimilarity, decimal distanceMeters,
        bool nameMatched, bool distanceMatched, bool routeTypeMatched,
        string? rawRouteType, string? canonicalRouteType,
        double autoNameThreshold, double autoDistThreshold,
        string status)
    {
        if (status == "AutoMerged")
            return "\u2705 AUTO-MERGED \u2014 all 3 criteria met";

        if (status == "NewStation")
            return "\u2139 NEW STATION \u2014 no suitable match found nearby";

        if (status == "ManuallyApproved")
            return "\u2705 MANUALLY APPROVED";

        if (status == "Rejected")
            return "\u2717 REJECTED";

        var failures = new List<string>();

        if (!nameMatched)
        {
            var pct = (nameSimilarity * 100).ToString("F0");
            failures.Add($"name {pct}% < {(autoNameThreshold * 100):F0}%");
        }

        if (!distanceMatched)
        {
            var d = distanceMeters < 1000
                ? distanceMeters.ToString("F0") + "m"
                : (distanceMeters / 1000).ToString("F2") + "km";
            failures.Add($"distance {d} > {autoDistThreshold:F0}m");
        }

        if (!routeTypeMatched)
        {
            if (rawRouteType is not null && canonicalRouteType is not null)
                failures.Add($"route type mismatch ({rawRouteType} vs {canonicalRouteType})");
            else
                failures.Add("route type mismatch");
        }

        if (failures.Count == 0)
            return "\u26A0 PENDING \u2014 unknown reason";

        return "\u274C PENDING \u2014 " + string.Join(", ", failures);
    }
}
