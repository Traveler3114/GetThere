using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Entities;

public class StopTime
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    // A GTFS stop id, tens of characters. This was 450 — the old ceiling picked as "the largest
    // value that still fits an index key", which is a limit rather than a size. TransitDbContext
    // configures 128 for this column; the two must agree, and the fluent call wins if they do not.
    [MaxLength(128)]
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
