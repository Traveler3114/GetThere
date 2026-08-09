using NetTopologySuite.Geometries;

using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Core;

/// <summary>
/// The normalized network a feed version imports from, whatever it was read out of.
/// <para>
/// This is the contract between "where the data came from" and "how it gets written". It used to be
/// <c>FeedManager.ParsedGtfsData</c>, a private record produced by exactly one parser, but the
/// import phases already consumed it rather than the archive — so promoting it is what lets a
/// non-GTFS source feed the same pipeline without synthesizing a zip for it to re-parse.
/// </para>
/// <para>
/// Every section is nullable, and null means <em>the source does not carry this</em> — distinct from
/// an empty list, which means it carries the section and it happens to be empty. Import phases skip
/// a null section rather than failing on it. Stop times are deliberately absent here: they are the
/// largest thing in a feed by an order of magnitude and stay a stream on
/// <see cref="SourceReadResult"/>.
/// </para>
/// </summary>
public class TransitDocument
{
    public required int OperatorId { get; init; }

    public List<RawAgencyRecord>? Agencies { get; init; }
    public List<RawStopRecord>? Stops { get; init; }
    public List<RawRouteRecord>? Routes { get; init; }
    public List<RawTripRecord>? Trips { get; init; }
    public List<RawCalendarRecord>? Calendar { get; init; }
    public List<RawCalendarDateRecord>? CalendarDates { get; init; }

    /// <summary>
    /// Not nullable, because it is written to as well as read: shape auto-generation and manual-edit
    /// carry-forward both add entries. Empty means no shapes, which is what a feed without
    /// shapes.txt has always produced.
    /// </summary>
    public Dictionary<string, LineString> Shapes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <see cref="SourceReadResult.StopTimes"/> will yield anything.</summary>
    public bool HasStopTimes { get; init; }

    /// <summary>
    /// The mode to attribute to a stop that no stop time reaches.
    /// <para>
    /// Reconciliation needs a route type per stop — it goes into the Onestop id and into match
    /// scoring — and normally derives it by walking stop_times to see which route serves the stop.
    /// A feed with no timetable has nothing to walk, so without this every one of its stops is
    /// parked as <c>Inactive</c> and never reaches the map. Only set when the answer is
    /// unambiguous; null means abstain, which leaves the old behaviour intact.
    /// </para>
    /// </summary>
    public RouteType? DefaultRouteType { get; init; }

    /// <summary>
    /// Sections filled in by a completer rather than read from the source, so the import log and the
    /// admin console can say which parts of a feed were inferred.
    /// </summary>
    public List<string> SynthesizedSections { get; } = [];

    private Dictionary<string, RouteType>? _routeTypeByRoute;
    private Dictionary<string, string>? _routeByTrip;

    public Dictionary<string, RouteType> RouteTypeByRoute => _routeTypeByRoute ??= (Routes ?? [])
        .GroupBy(r => r.RouteId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().RouteTypeEnum, StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> RouteByTrip => _routeByTrip ??= (Trips ?? [])
        .GroupBy(t => t.TripId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().RouteId, StringComparer.OrdinalIgnoreCase);

    public FeedCompleteness Completeness =>
        Trips is { Count: > 0 } && HasStopTimes ? FeedCompleteness.Schedule
        : Routes is { Count: > 0 } ? FeedCompleteness.Network
        : FeedCompleteness.StopsOnly;

    public string Summary() =>
        $"{Count(Agencies)} agencies, {Count(Stops)} stops, {Count(Routes)} routes, {Count(Trips)} trips, "
        + $"{Count(Calendar)} calendars, {Count(CalendarDates)} calendar_dates, {Shapes.Count} shapes";

    private static string Count<T>(List<T>? section) =>
        section is null ? "no" : section.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
