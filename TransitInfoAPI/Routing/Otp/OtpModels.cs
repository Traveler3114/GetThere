namespace TransitInfoAPI.Routing.Otp;

/// <summary>
/// A journey-planning request. Beyond the two coordinates and time, it carries the request-shaping
/// knobs that drive preferences and preset filters ("no trains", greener, fewest transfers, …). All
/// of these are passed straight through to OTP; the fields default to unset, so an unfiltered request
/// behaves exactly as before.
/// </summary>
public sealed record PlanRequest
{
    public double FromLat { get; init; }
    public double FromLon { get; init; }
    public double ToLat { get; init; }
    public double ToLon { get; init; }

    /// <summary>Departure time; defaults to "now" when null. Interpreted in the configured timezone.</summary>
    public DateTimeOffset? DepartAt { get; init; }

    public int NumItineraries { get; init; } = 3;

    /// <summary>Treat the time as the desired <b>arrival</b> rather than departure.</summary>
    public bool ArriveBy { get; init; }

    /// <summary>
    /// Allowed transit sub-modes as OTP <c>Mode</c> names (e.g. <c>BUS</c>, <c>TRAM</c>, <c>RAIL</c>).
    /// Null/empty = all transit. This is how mode-exclude filters work: "no trains" passes every
    /// transit mode except <c>RAIL</c>/<c>SUBWAY</c>; "no planes" drops <c>AIRPLANE</c>.
    /// </summary>
    public IReadOnlyList<string>? TransitModes { get; init; }

    /// <summary>Include bike rental (e.g. Nextbike) as an access/egress mode. Default on.</summary>
    public bool IncludeBikeRental { get; init; } = true;

    /// <summary>How much walking is disliked relative to transit time (OTP default ~2.0). Higher = less walking.</summary>
    public double? WalkReluctance { get; init; }

    /// <summary>Flat penalty (seconds) added per transfer — high values push "fewest transfers".</summary>
    public int? TransferPenalty { get; init; }

    /// <summary>Restrict to wheelchair-accessible routing.</summary>
    public bool Wheelchair { get; init; }

    /// <summary>
    /// A named filter preset (<c>fastest</c>, <c>greener</c>, <c>fewest-transfers</c>,
    /// <c>least-walking</c>, <c>balanced</c>, <c>no-trains</c>, <c>no-planes</c>). Resolved to a
    /// <see cref="RoutingPreference"/> and applied by the controller. Ignored if <see cref="Preference"/>
    /// is supplied.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>An explicit structured preference (what an AI layer would emit); overrides <see cref="Preset"/>.</summary>
    public RoutingPreference? Preference { get; init; }
}

/// <summary>One planned itinerary returned to the caller.</summary>
public sealed record PlanItineraryDto(
    int DurationSeconds,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    double WalkDistanceMeters,
    IReadOnlyList<PlanLegDto> Legs);

/// <summary>
/// One leg of an itinerary. <see cref="OperatorGlobalId"/> is the join that keeps a future
/// "buy this ticket" hook an addition rather than a rewrite: it is the operator's TransitInfo
/// GlobalId (the bundle's <c>agency_id</c>), which GetThereAPI's
/// <c>TicketingAdapter.TransitInfoGlobalId</c> already references. The client can read it off the leg
/// and ask GetThereAPI whether a ticket is purchasable — without either server calling the other.
/// </summary>
public sealed record PlanLegDto(
    string Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    double DistanceMeters,
    bool IsTransit,
    PlanPlaceDto From,
    PlanPlaceDto To,
    string? RouteShortName,
    string? RouteLongName,
    string? OperatorGlobalId,
    string? TripGtfsId,
    bool RealtimeState,
    string? Geometry);

public sealed record PlanPlaceDto(string? Name, double Lat, double Lon);

/// <summary>Thrown when OTP returns GraphQL errors or an unusable response.</summary>
public sealed class OtpPlanException(string message) : Exception(message);
