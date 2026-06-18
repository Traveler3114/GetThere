using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Managers;

public class ReconciliationManager
{
    private readonly TransitDbContext _db;
    private readonly ILogger<ReconciliationManager> _logger;
    private readonly OnestopIdManager _onestopId;
    private readonly IConfiguration _config;

    public ReconciliationManager(
        TransitDbContext db,
        ILogger<ReconciliationManager> logger,
        OnestopIdManager onestopId,
        IConfiguration config)
    {
        _db = db;
        _logger = logger;
        _onestopId = onestopId;
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
            .ThenInclude(f => f.Operator)
            .FirstAsync(fv => fv.Id == feedVersionId, ct);

        var minLat = rawStops.Min(r => r.Lat);
        var maxLat = rawStops.Max(r => r.Lat);
        var minLon = rawStops.Min(r => r.Lon);
        var maxLon = rawStops.Max(r => r.Lon);
        var buffer = 0.005;
        var existingStations = await _db.CanonicalStations
            .Where(cs => cs.IsActive
                && cs.Latitude >= minLat - buffer && cs.Latitude <= maxLat + buffer
                && cs.Longitude >= minLon - buffer && cs.Longitude <= maxLon + buffer)
            .ToListAsync(ct);

        var inactiveStations = await _db.CanonicalStations
            .Where(cs => !cs.IsActive)
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
            var onestopId = _onestopId.GenerateStopOnestopId(rawStop.Lat, rawStop.Lon, rawStop.Name, rawStop.RouteType);

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
                inactiveStation.PrimaryRouteType = rawStop.RouteType;
                inactiveStation.StationType = rawStop.StationType;
                rawStop.CanonicalStationId = inactiveStation.Id;
                onestopToStation[onestopId] = inactiveStation;
                newStationList.Add(inactiveStation);
                continue;
            }

            var nearbyMatch = existingStations
                .FirstOrDefault(s => s.PrimaryRouteType == rawStop.RouteType
                    && CalculateDistanceMeters(rawStop.Lat, rawStop.Lon, s.Latitude, s.Longitude) <= 50
                    && CalculateNameSimilarity(rawStop.Name, s.Name) >= 0.85);

            if (nearbyMatch is not null)
            {
                onestopToStation[onestopId] = nearbyMatch;
                continue;
            }

            var station = new CanonicalStation
            {
                GlobalId = $"gt-raw-{rawStop.Id}",
                OnestopId = onestopId,
                Name = rawStop.Name,
                Latitude = rawStop.Lat,
                Longitude = rawStop.Lon,
                StationType = rawStop.StationType,
                PrimaryRouteType = rawStop.RouteType,
                IsActive = true,
                CountryId = feedVersion.Feed.Operator.CountryId,
                CreatedAt = DateTime.UtcNow
            };
            _db.CanonicalStations.Add(station);
            newStationList.Add(station);
        }

        await _db.SaveChangesAsync(ct);

        foreach (var s in newStationList)
            onestopToStation[s.OnestopId] = s;

        existingStations = await _db.CanonicalStations
            .Where(cs => cs.IsActive
                && cs.Latitude >= minLat - buffer && cs.Latitude <= maxLat + buffer
                && cs.Longitude >= minLon - buffer && cs.Longitude <= maxLon + buffer)
            .ToListAsync(ct);

        // Phase 2: match raw stops to stations and create candidates
        var existingLinkedStationIds = await _db.CanonicalStationOperators
            .Where(cso => cso.OperatorId == feedOperatorId)
            .Select(cso => cso.CanonicalStationId)
            .ToHashSetAsync(ct);
        var addedOperatorLinks = new HashSet<(int CanonicalStationId, int OperatorId)>(
            existingLinkedStationIds.Select(id => (id, feedOperatorId)));

        foreach (var rawStop in rawStops)
        {
            var onestopId = _onestopId.GenerateStopOnestopId(rawStop.Lat, rawStop.Lon, rawStop.Name, rawStop.RouteType);
            var station = onestopToStation[onestopId];
            rawStop.CanonicalStationId = station.Id;

            var match = FindBestMatch(
                rawStop.Name, rawStop.Lat, rawStop.Lon, rawStop.RouteType,
                existingStations, autoDistThreshold * 2);

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
                    RawRouteType = rawStop.RouteType,
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
                    CreatedAt = DateTime.UtcNow
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
                    RawRouteType = rawStop.RouteType,
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
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                rawStop.CanonicalStationId = station.Id;
                rawStop.ReconciliationStatus = ReconciliationStatus.NewStation;

                _db.ReconciliationCandidates.Add(new ReconciliationCandidate
                {
                    RawStopId = rawStop.Id,
                    RawStopName = rawStop.Name,
                    RawStopLat = rawStop.Lat,
                    RawStopLon = rawStop.Lon,
                    RawRouteType = rawStop.RouteType,
                    FeedId = feedVersion.FeedId,
                    SuggestedCanonicalStationId = station.Id,
                    ConfidenceScore = 1.0m,
                    NameSimilarityScore = 1.0m,
                    DistanceMeters = 0,
                    NameMatched = false,
                    DistanceMatched = false,
                    RouteTypeMatched = true,
                    AutoReconciled = true,
                    Status = ReconciliationStatus.NewStation,
                    CreatedAt = DateTime.UtcNow
                });

                if (addedOperatorLinks.Add((station.Id, feedOperatorId)))
                    _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                    {
                        CanonicalStationId = station.Id,
                        OperatorId = feedOperatorId
                    });
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Reconciled {Count} raw stops for FeedVersion {FeedVersionId}", rawStops.Count, feedVersionId);
    }

    public async Task<CanonicalStation> CreateCanonicalStationAsync(RawStop rawStop, int countryId, CancellationToken ct)
    {
        var onestopId = _onestopId.GenerateStopOnestopId(rawStop.Lat, rawStop.Lon, rawStop.Name, rawStop.RouteType);

        var station = new CanonicalStation
        {
            GlobalId = $"gt-raw-{rawStop.Id}",
            OnestopId = onestopId,
            Name = rawStop.Name,
            Latitude = rawStop.Lat,
            Longitude = rawStop.Lon,
            StationType = rawStop.StationType,
            PrimaryRouteType = rawStop.RouteType,
            IsActive = true,
            CountryId = countryId,
            CreatedAt = DateTime.UtcNow
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
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> ApproveCandidateAsync(int id, CancellationToken ct)
    {
        var candidate = await _db.ReconciliationCandidates
            .Include(c => c.Feed)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null)
            return false;

        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        if (candidate.SuggestedCanonicalStationId.HasValue)
        {
            var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
            if (rawStop is not null)
            {
                rawStop.CanonicalStationId = candidate.SuggestedCanonicalStationId;
                rawStop.ReconciliationStatus = ReconciliationStatus.ManuallyApproved;
            }

            await LinkOperatorToStationAsync(candidate.SuggestedCanonicalStationId.Value, candidate.Feed.OperatorId, ct);
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectCandidateAsync(int id, bool createNewStation = false, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates
            .Include(c => c.Feed)
            .ThenInclude(c => c.Operator)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null)
            return false;

        if (createNewStation)
        {
            var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
            if (rawStop is not null)
            {
                var newStation = await CreateCanonicalStationAsync(rawStop, candidate.Feed.Operator.CountryId, ct);
                candidate.SuggestedCanonicalStationId = newStation.Id;
                rawStop.CanonicalStationId = newStation.Id;
                rawStop.ReconciliationStatus = ReconciliationStatus.NewStation;
            }
        }

        candidate.Status = ReconciliationStatus.Rejected;
        candidate.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReassignCandidateAsync(int id, int canonicalStationId, CancellationToken ct)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync([id], ct);
        if (candidate is null)
            return false;

        var station = await _db.CanonicalStations.FindAsync([canonicalStationId], ct);
        if (station is null)
            return false;

        candidate.SuggestedCanonicalStationId = canonicalStationId;
        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        var rawStop = await _db.RawStops.FindAsync([candidate.RawStopId], ct);
        if (rawStop is not null)
        {
            rawStop.CanonicalStationId = canonicalStationId;
            rawStop.ReconciliationStatus = ReconciliationStatus.ManuallyApproved;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private (CanonicalStation Station, double NameScore, double Distance, bool RouteTypeMatch)? FindBestMatch(
        string rawName, double rawLat, double rawLon, RouteType rawRouteType,
        List<CanonicalStation> stations, double searchRadiusMeters)
    {
        (CanonicalStation Station, double NameScore, double Distance, bool RouteTypeMatch)? best = null;

        foreach (var station in stations.Where(s => s.PrimaryRouteType == rawRouteType))
        {
            var dist = CalculateDistanceMeters(rawLat, rawLon, station.Latitude, station.Longitude);
            if (dist > searchRadiusMeters) continue;

            var nameScore = CalculateNameSimilarity(rawName, station.Name);
            if (nameScore < 0.3) continue;

            if (best is null || nameScore > best.Value.NameScore)
            {
                best = (station, nameScore, dist, true);
            }
        }

        return best;
    }

    public double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }

    public double CalculateNameSimilarity(string name1, string name2)
    {
        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return 0;

        var n1 = NormalizeName(name1);
        var n2 = NormalizeName(name2);

        if (n1 == n2) return 1.0;
        if (n1.Contains(n2) || n2.Contains(n1)) return 0.85;

        var dist = LevenshteinDistance(n1, n2);
        var maxLen = Math.Max(n1.Length, n2.Length);
        if (maxLen == 0) return 0;

        return 1.0 - (double)dist / maxLen;
    }

    private static string NormalizeName(string name)
    {
        var lower = name.ToLowerInvariant().Trim();

        lower = lower
            .Replace("kol.", "kolodvor")
            .Replace("trg", "trg")
            .Replace("ul.", "ulica")
            .Replace("st.", "sveti")
            .Replace("sv.", "sveti");

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
}
