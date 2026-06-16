using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Entities;

public class StopTime
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    [MaxLength(450)]
    public string RawStopId { get; set; } = string.Empty;
    public int? RawStopEntityId { get; set; }
    public RawStop? RawStopEntity { get; set; }
    public int? CanonicalStationId { get; set; }
    public CanonicalStation? CanonicalStation { get; set; }

    public int ArrivalTime { get; set; }
    public int DepartureTime { get; set; }
    public int StopSequence { get; set; }
    public string? StopHeadsign { get; set; }
    public int? PickupType { get; set; }
    public int? DropOffType { get; set; }
    public bool? Timepoint { get; set; }
}
