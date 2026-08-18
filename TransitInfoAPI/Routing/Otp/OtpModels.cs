namespace TransitInfoAPI.Routing.Otp;

/// <summary>A journey-planning request: two coordinates, an optional departure time, and a count.</summary>
public sealed record PlanRequest
{
    public double FromLat { get; init; }
    public double FromLon { get; init; }
    public double ToLat { get; init; }
    public double ToLon { get; init; }

    /// <summary>Departure time; defaults to "now" when null. Interpreted in the configured timezone.</summary>
    public DateTimeOffset? DepartAt { get; init; }

    public int NumItineraries { get; init; } = 3;
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
    bool RealtimeState);

public sealed record PlanPlaceDto(string? Name, double Lat, double Lon);

/// <summary>Thrown when OTP returns GraphQL errors or an unusable response.</summary>
public sealed class OtpPlanException(string message) : Exception(message);
