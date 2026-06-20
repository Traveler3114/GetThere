using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class RawStop
{
    public int Id { get; set; }
    public int FeedVersionId { get; set; }
    public FeedVersion FeedVersion { get; set; } = null!;

    public string RawStopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public StationType StationType { get; set; }
    public string? ParentRawStopId { get; set; }
    public string? StopCode { get; set; }
    public string? StopDesc { get; set; }
    public string? ZoneId { get; set; }
    public string? PlatformCode { get; set; }
    public bool? WheelchairBoarding { get; set; }

    public RouteType? RouteType { get; set; }
    public bool IsActive { get; set; } = true;

    public int? CanonicalStationId { get; set; }
    public CanonicalStation? CanonicalStation { get; set; }
    public ReconciliationStatus ReconciliationStatus { get; set; }
}
