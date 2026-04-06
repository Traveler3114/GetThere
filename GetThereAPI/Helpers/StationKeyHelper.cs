namespace GetThereAPI.Helpers;

public static class StationKeyHelper
{
    public static string Build(string? parentStationId, string? name, double lat, double lon)
    {
        if (!string.IsNullOrWhiteSpace(parentStationId))
            return $"parent:{parentStationId.Trim()}";

        var normalizedName = NormalizeName(name);
        return $"geo:{normalizedName}:{Math.Round(lat, 4):F4}:{Math.Round(lon, 4):F4}";
    }

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        var chars = name.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray();
        var collapsed = string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(collapsed) ? "unknown" : collapsed;
    }
}
