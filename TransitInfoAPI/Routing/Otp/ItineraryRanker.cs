namespace TransitInfoAPI.Routing.Otp;

/// <summary>
/// Re-orders the Pareto set OTP returns by a chosen objective. OTP already produces diverse
/// itineraries; this picks which one leads. Pure and operator-agnostic — the "greener" estimate is
/// mode-based (no operator or country literals), a rough ranking signal rather than a reported figure.
/// </summary>
public static class ItineraryRanker
{
    public static IReadOnlyList<PlanItineraryDto> Rank(IReadOnlyList<PlanItineraryDto> itineraries, RankBy rankBy)
    {
        if (itineraries.Count <= 1)
            return itineraries;

        return rankBy switch
        {
            RankBy.Fastest => [.. itineraries.OrderBy(i => i.DurationSeconds)],
            RankBy.FewestTransfers => [.. itineraries.OrderBy(TransferCount).ThenBy(i => i.DurationSeconds)],
            RankBy.LeastWalking => [.. itineraries.OrderBy(i => i.WalkDistanceMeters).ThenBy(i => i.DurationSeconds)],
            RankBy.Greener => [.. itineraries.OrderBy(EstimatedEmissionGrams).ThenBy(i => i.DurationSeconds)],
            RankBy.Balanced => RankBalanced(itineraries),
            _ => itineraries,
        };
    }

    /// <summary>Transit legs minus one — a walk-only itinerary has zero transfers.</summary>
    public static int TransferCount(PlanItineraryDto itinerary) =>
        Math.Max(0, itinerary.Legs.Count(l => l.IsTransit) - 1);

    /// <summary>Rough total grams of CO₂, distance × a mode factor. For ranking only.</summary>
    public static double EstimatedEmissionGrams(PlanItineraryDto itinerary) =>
        itinerary.Legs.Sum(l => l.DistanceMeters * EmissionFactor(l.Mode));

    // Grams CO₂ per metre by mode. Rounded, mode-based, deliberately not authoritative.
    private static double EmissionFactor(string? mode) => (mode ?? string.Empty).ToUpperInvariant() switch
    {
        "WALK" or "BICYCLE" or "SCOOTER" => 0.0,
        "TRAM" or "SUBWAY" or "RAIL" or "FUNICULAR" or "GONDOLA" or "CABLE_CAR" or "TROLLEYBUS" or "MONORAIL" => 0.04,
        "BUS" or "COACH" => 0.10,
        "CAR" => 0.17,
        "FERRY" => 0.19,
        "AIRPLANE" => 0.25,
        _ => 0.08,
    };

    // Normalise each metric to [0,1] across the set, then order by an equal-ish weighted sum.
    private static IReadOnlyList<PlanItineraryDto> RankBalanced(IReadOnlyList<PlanItineraryDto> itineraries)
    {
        double MaxOr1(Func<PlanItineraryDto, double> f) { var m = itineraries.Max(f); return m <= 0 ? 1 : m; }

        var maxDur = MaxOr1(i => i.DurationSeconds);
        var maxTransfers = MaxOr1(i => TransferCount(i));
        var maxWalk = MaxOr1(i => i.WalkDistanceMeters);
        var maxEmission = MaxOr1(EstimatedEmissionGrams);

        double Score(PlanItineraryDto i) =>
            0.40 * (i.DurationSeconds / maxDur)
            + 0.20 * (TransferCount(i) / maxTransfers)
            + 0.20 * (i.WalkDistanceMeters / maxWalk)
            + 0.20 * (EstimatedEmissionGrams(i) / maxEmission);

        return [.. itineraries.OrderBy(Score)];
    }
}
