using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Core;

/// <summary>
/// A realtime source whose data is derived rather than fetched — no endpoint to map, so
/// configuration cannot describe it. Matched against <see cref="CustomSource.ExtractorKey"/>.
/// </summary>
public interface IRealtimeExtractor
{
    string Key { get; }
    string Description { get; }
    Task<List<VehicleResponse>> ExtractAsync(CustomSource source, Feed feed, CancellationToken ct);
}

public class RealtimeExtractorRegistry
{
    private readonly Dictionary<string, IRealtimeExtractor> _byKey;

    public RealtimeExtractorRegistry(IEnumerable<IRealtimeExtractor> extractors)
    {
        _byKey = new Dictionary<string, IRealtimeExtractor>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractor in extractors) _byKey[extractor.Key] = extractor;
    }

    public IRealtimeExtractor? For(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : _byKey.GetValueOrDefault(key);
}
