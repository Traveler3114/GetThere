namespace TransitInfoAPI.Routing.Otp;

/// <summary>How the returned itineraries are ordered. Cost ("cheapest") is deliberately absent — fares
/// live in GetThereAPI, so the app ranks by price after quoting.</summary>
public enum RankBy
{
    Fastest,
    FewestTransfers,
    LeastWalking,
    Greener,
    Balanced,
}

/// <summary>
/// A structured routing preference — the intent behind a filter/preset, kept explicit so the very same
/// object can later be produced by an AI "interpret my preference" layer with no rework. It only
/// *shapes the request* (which modes, which penalties) and *picks the ranking*; routing itself stays
/// deterministic in OTP.
/// </summary>
public sealed record RoutingPreference
{
    public RankBy RankBy { get; init; } = RankBy.Fastest;

    /// <summary>OTP <c>Mode</c> names to exclude from transit (e.g. <c>RAIL</c>, <c>AIRPLANE</c>).</summary>
    public IReadOnlyList<string>? ExcludeModes { get; init; }

    public double? WalkReluctance { get; init; }
    public int? TransferPenalty { get; init; }
}

/// <summary>
/// Named preset filters. Each is just a bundle of (request-shaping + ranking) — the same shape an AI
/// layer would emit. Unknown/empty presets resolve to null (caller falls back to the default fastest).
/// "cheapest" intentionally has no server-side preset: the app ranks by the GetThereAPI journey total.
/// </summary>
public static class RoutingPresets
{
    /// <summary>The transit modes routing considers; a mode-exclude subtracts from this set.</summary>
    public static readonly IReadOnlyList<string> AllTransitModes =
        ["TRAM", "SUBWAY", "RAIL", "BUS", "FERRY", "CABLE_CAR", "GONDOLA", "FUNICULAR", "TROLLEYBUS", "MONORAIL"];

    public static RoutingPreference? Resolve(string? preset) => Normalize(preset) switch
    {
        "" or "fastest" => new RoutingPreference { RankBy = RankBy.Fastest },
        "fewesttransfers" => new RoutingPreference { RankBy = RankBy.FewestTransfers, TransferPenalty = 1800 },
        "leastwalking" => new RoutingPreference { RankBy = RankBy.LeastWalking, WalkReluctance = 5.0 },
        "greener" => new RoutingPreference { RankBy = RankBy.Greener },
        "balanced" => new RoutingPreference { RankBy = RankBy.Balanced },
        "notrains" => new RoutingPreference { ExcludeModes = ["RAIL", "SUBWAY"] },
        "noplanes" => new RoutingPreference { ExcludeModes = ["AIRPLANE"] },
        _ => null,
    };

    /// <summary>The transit allow-list for a preference: all transit modes minus its excludes.</summary>
    public static IReadOnlyList<string>? AllowedTransitModes(RoutingPreference? preference)
    {
        if (preference?.ExcludeModes is not { Count: > 0 } excludes)
            return null; // null = all transit (the default)

        var banned = new HashSet<string>(excludes, StringComparer.OrdinalIgnoreCase);
        return AllTransitModes.Where(m => !banned.Contains(m)).ToList();
    }

    private static string Normalize(string? preset) =>
        (preset ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
}
