namespace TransitInfoAPI.Enums;

/// <summary>
/// How much of a transit network a feed version actually carries.
/// <para>
/// A GTFS archive is always <see cref="Schedule"/> — the parser rejects one missing trips or
/// stop_times. Custom sources are the reason this exists: an operator that publishes a stop list and
/// nothing else is a real, useful feed, and it should import as what it is rather than fail
/// validation for the files it was never going to have.
/// </para>
/// </summary>
public enum FeedCompleteness
{
    /// <summary>Stops only. Renders on the map; resolves no routes and no departures.</summary>
    StopsOnly,

    /// <summary>Stops and routes, but no timetable — the shape of the network without its schedule.</summary>
    Network,

    /// <summary>A full timetable: trips and stop times on top of the network.</summary>
    Schedule
}
