namespace TransitInfoAPI.Models;

public class ReconciliationDetailDto : ReconciliationDto
{
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByAdminId { get; set; }
    public string? RawStopCountry { get; set; }
    public StationDetailDto? RawStopDetail { get; set; }
    public StationDetailDto? SuggestedStationDetail { get; set; }
    public string AutoMergeVerdict { get; set; } = string.Empty;
}
