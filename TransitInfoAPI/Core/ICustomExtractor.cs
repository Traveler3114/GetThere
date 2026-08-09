using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Core;

/// <summary>
/// A hand-written reader for an operator whose data cannot be described as configuration.
/// <para>
/// The declarative engine covers the shapes that are worth describing — an endpoint returning rows,
/// paginated, with fields that map onto a network. Past that the alternatives are a configuration
/// language that grows into a bad programming language, or a class. This is the class: register an
/// implementation, put its <see cref="Key"/> on the source, and the engine steps aside entirely.
/// </para>
/// <para>
/// Implementations get the raw <see cref="HttpClient"/> and the source row, and owe a document back.
/// Everything downstream — completion, versioning, the import pipeline — is unchanged, so an
/// extractor only has to solve the part that was actually hard.
/// </para>
/// </summary>
public interface ICustomExtractor
{
    /// <summary>Matched against <see cref="CustomSource.ExtractorKey"/>. Stable — it lives in the database.</summary>
    string Key { get; }

    /// <summary>Human-readable, for the admin console's extractor picker.</summary>
    string Description { get; }

    Task<TransitDocument> ExtractAsync(CustomSource source, HttpClient http, CancellationToken ct);
}

/// <summary>
/// Resolves an <see cref="ICustomExtractor"/> by key. Registration is plain DI: add an
/// <c>ICustomExtractor</c> and it becomes available to every source.
/// </summary>
public class CustomExtractorRegistry
{
    private readonly Dictionary<string, ICustomExtractor> _byKey;

    public CustomExtractorRegistry(IEnumerable<ICustomExtractor> extractors)
    {
        _byKey = new Dictionary<string, ICustomExtractor>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractor in extractors)
            _byKey[extractor.Key] = extractor;
    }

    public ICustomExtractor? For(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : _byKey.GetValueOrDefault(key);

    public IReadOnlyCollection<ICustomExtractor> All => _byKey.Values;
}
