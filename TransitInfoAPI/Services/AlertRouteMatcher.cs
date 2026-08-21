using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;

namespace TransitInfoAPI.Services;

public sealed partial class AlertRouteMatcher
{
    // Croatian phrasings observed in live notices (2026-08-21):
    //   linija 12A, linije 6, 8 i 14, linij br. 33, Tramvajska linija T1
    private static readonly Regex LineNumberRegex = CreateLineRegex();

    [GeneratedRegex(@"(?:linij\w*\s*(?:br(?:oj)?\.?\s*)?([0-9]+[A-Z]?(?:\s*[,\si]+\s*[0-9]+[A-Z]?)*)|Tramvajsk\w*\s+linij\w*\s*([A-Z]?[0-9]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateLineRegex();

    // Further comma / "i" separated list inside the match is split manually.
    [GeneratedRegex(@"[0-9]+[A-Z]?|[A-Z][0-9]+")]
    private static partial Regex TokenExtractRegex();

    private readonly Dictionary<int, Dictionary<string, int>> _routeLookupByOperator = [];
    private readonly TransitDbContext _db;
    private readonly ILogger<AlertRouteMatcher> _logger;

    public AlertRouteMatcher(TransitDbContext db, ILogger<AlertRouteMatcher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string?> MatchAsync(string? title, string? description, int? operatorId, CancellationToken ct = default)
    {
        var combined = string.Join(" ", new[] { title, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(combined))
            return null;

        var tokens = ExtractTokens(combined);
        if (tokens.Count == 0)
            return null;

        // Resolve tokens against CanonicalRoute.ShortName scoped to OperatorId when possible.
        // Route short names ("1", "2", "12A") repeat across dozens of operators, so an unscoped match
        // would resolve a ZET notice to whichever operator's route 1 happened to come back first —
        // and that wrong route id would reach OTP as a real disruption. Refuse rather than guess.
        if (!operatorId.HasValue)
        {
            _logger.LogWarning(
                "AlertRouteMatcher: no operator resolved, skipping route match for tokens [{Tokens}]",
                string.Join(",", tokens));
            return null;
        }

        var dict = await GetRouteLookupAsync(operatorId.Value, ct);

        var matchedIds = new List<string>();
        foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (dict.TryGetValue(token, out var routeId))
                matchedIds.Add(routeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (dict.TryGetValue(token.TrimStart('0'), out var routeId2))
                matchedIds.Add(routeId2.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (matchedIds.Count == 0)
        {
            _logger.LogDebug("AlertRouteMatcher: no route matched for tokens [{Tokens}] operator {OperatorId}", string.Join(",", tokens), operatorId);
            return null;
        }

        return string.Join(",", matchedIds.Distinct());
    }

    /// <summary>
    /// Short name → canonical route id for one operator, read once per matcher instance.
    /// <para>
    /// The worker builds one matcher per source and then calls <see cref="MatchAsync"/> for every
    /// notice on that page, so querying inside the match made this an N+1: one
    /// <c>CanonicalRoutes</c> read per alert, ~180 per poll across all sources. Under load those
    /// piled up — a single one was measured at 62 s — and starved the connection pool until
    /// unrelated admin pages stopped loading. One read per operator fixes it.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, int>> GetRouteLookupAsync(int operatorId, CancellationToken ct)
    {
        if (_routeLookupByOperator.TryGetValue(operatorId, out var cached))
            return cached;

        var candidates = await _db.CanonicalRoutes.AsNoTracking()
            .Where(r => r.IsActive && r.OperatorId == operatorId)
            .Select(r => new { r.Id, r.ShortName })
            .ToListAsync(ct);

        var lookup = candidates
            .Where(r => !string.IsNullOrWhiteSpace(r.ShortName))
            .GroupBy(r => r.ShortName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        _routeLookupByOperator[operatorId] = lookup;
        return lookup;
    }

    public static IReadOnlyList<string> ExtractTokens(string text)
    {
        var result = new List<string>();
        foreach (Match m in LineNumberRegex.Matches(text))
        {
            var groupValue = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
            if (string.IsNullOrEmpty(groupValue)) continue;

            foreach (Match extra in TokenExtractRegex().Matches(groupValue))
            {
                result.Add(extra.Value.Trim().ToUpperInvariant());
            }
        }
        return result;
    }
}
