using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services;

public class ReconciliationService
{
    private readonly TransitDbContext _db;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(TransitDbContext db, ILogger<ReconciliationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ReconcileFeedStopsAsync(int feedId, List<(string stopId, string stopName, double lat, double lon)> rawStops, CancellationToken ct = default)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { feedId }, ct);
        if (feed is null) return;

        var existingStations = await _db.CanonicalStations
            .Where(cs => cs.IsActive)
            .ToListAsync(ct);

        foreach (var raw in rawStops)
        {
            var bestMatch = FindBestMatch(raw, existingStations);

            if (bestMatch is null)
            {
                var newStation = new CanonicalStation
                {
                    GlobalId = $"gt-{feed.FeedId}-{raw.stopId.ToLowerInvariant()}",
                    Name = raw.stopName,
                    Latitude = raw.lat,
                    Longitude = raw.lon,
                    StationType = StationType.Stop,
                    IsActive = true,
                    CountryId = 1,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CanonicalStations.Add(newStation);
                await _db.SaveChangesAsync(ct);

                _db.ReconciliationCandidates.Add(new ReconciliationCandidate
                {
                    RawStopId = raw.stopId,
                    RawStopName = raw.stopName,
                    RawStopLat = raw.lat,
                    RawStopLon = raw.lon,
                    FeedId = feedId,
                    SuggestedCanonicalStationId = newStation.Id,
                    ConfidenceScore = 1.0m,
                    NameSimilarityScore = 1.0m,
                    DistanceMeters = 0,
                    Status = ReconciliationStatus.NewStation,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else if (bestMatch.Value.Confidence >= 0.85m)
            {
                _db.ReconciliationCandidates.Add(new ReconciliationCandidate
                {
                    RawStopId = raw.stopId,
                    RawStopName = raw.stopName,
                    RawStopLat = raw.lat,
                    RawStopLon = raw.lon,
                    FeedId = feedId,
                    SuggestedCanonicalStationId = bestMatch.Value.Station.Id,
                    ConfidenceScore = bestMatch.Value.Confidence,
                    NameSimilarityScore = bestMatch.Value.NameScore,
                    DistanceMeters = bestMatch.Value.Distance,
                    Status = ReconciliationStatus.AutoMerged,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                _db.ReconciliationCandidates.Add(new ReconciliationCandidate
                {
                    RawStopId = raw.stopId,
                    RawStopName = raw.stopName,
                    RawStopLat = raw.lat,
                    RawStopLon = raw.lon,
                    FeedId = feedId,
                    SuggestedCanonicalStationId = bestMatch.Value.Station.Id,
                    ConfidenceScore = bestMatch.Value.Confidence,
                    NameSimilarityScore = bestMatch.Value.NameScore,
                    DistanceMeters = bestMatch.Value.Distance,
                    Status = ReconciliationStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Reconciled {Count} raw stops for feed {FeedId}", rawStops.Count, feedId);
    }

    private (CanonicalStation Station, decimal Confidence, decimal NameScore, decimal Distance)? FindBestMatch(
        (string stopId, string stopName, double lat, double lon) raw,
        List<CanonicalStation> stations)
    {
        (CanonicalStation Station, decimal Confidence, decimal NameScore, decimal Distance)? best = null;

        foreach (var station in stations)
        {
            var dist = (decimal)CalculateDistance(raw.lat, raw.lon, station.Latitude, station.Longitude);
            var nameScore = CalculateNameSimilarity(raw.stopName, station.Name);

            var confidence = nameScore * 0.7m + (dist < 100 ? 0.3m : dist < 500 ? 0.15m : 0);

            if (best is null || confidence > best.Value.Confidence)
                best = (Station: station, Confidence: confidence, NameScore: nameScore, Distance: dist);
        }

        return best;
    }

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
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

    private static decimal CalculateNameSimilarity(string name1, string name2)
    {
        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return 0;

        var n1 = name1.ToLowerInvariant().Trim();
        var n2 = name2.ToLowerInvariant().Trim();
        if (n1 == n2) return 1.0m;
        if (n1.Contains(n2) || n2.Contains(n1)) return 0.8m;

        var words1 = n1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words2 = n2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var common = words1.Intersect(words2).Count();
        return common / (decimal)Math.Max(words1.Length, words2.Length);
    }
}
