namespace GetThereShared.Dtos;

/// <summary>
/// One route direction at a stop — has a list of departure times for today.
/// </summary>
public class StopDepartureGroupDto
{
    /// <summary>GTFS route_id</summary>
    public string RouteId { get; set; } = "";

    /// <summary>Human-readable route number, e.g. "268" or "T1"</summary>
    public string ShortName { get; set; } = "";

    /// <summary>Destination headsign, e.g. "V. Gorica"</summary>
    public string Headsign { get; set; } = "";

    /// <summary>
    /// HH:MM departure strings sorted ascending.
    /// Times like "25:30" (next-calendar-day trips) are kept as-is so the
    /// UI can show them at the end of the list naturally.
    /// </summary>
    public List<string> Times { get; set; } = [];
}
