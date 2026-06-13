using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class ReconciliationCandidate
{
    public int Id { get; set; }
    public string RawStopId { get; set; } = string.Empty;
    public string RawStopName { get; set; } = string.Empty;
    public double RawStopLat { get; set; }
    public double RawStopLon { get; set; }
    public int? SuggestedCanonicalStationId { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal DistanceMeters { get; set; }
    public decimal NameSimilarityScore { get; set; }
    public ReconciliationStatus Status { get; set; }
    public string? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

    public CanonicalStation? SuggestedCanonicalStation { get; set; }
}
