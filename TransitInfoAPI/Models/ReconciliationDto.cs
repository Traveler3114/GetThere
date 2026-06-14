namespace TransitInfoAPI.Models;

public class ReconciliationDto
{
    public int Id { get; set; }
    public string RawStopId { get; set; } = string.Empty;
    public string RawStopName { get; set; } = string.Empty;
    public double RawStopLat { get; set; }
    public double RawStopLon { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal NameSimilarityScore { get; set; }
    public decimal DistanceMeters { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? FeedId { get; set; }
    public int? SuggestedStationId { get; set; }
    public string? SuggestedStationName { get; set; }
    public double? SuggestedStationLat { get; set; }
    public double? SuggestedStationLon { get; set; }
}
