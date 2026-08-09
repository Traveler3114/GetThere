using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Core;

/// <summary>
/// Picks the <see cref="ITransitSource"/> that owns a feed.
/// <para>
/// The previous abstraction had one implementation and was never resolved through the interface —
/// <c>FeedManager</c> took the concrete class and used it directly, so adding a second source kind
/// meant editing the manager's constructor. Registration order decides ties; first match wins.
/// </para>
/// </summary>
public class TransitSourceResolver
{
    private readonly IEnumerable<ITransitSource> _sources;

    public TransitSourceResolver(IEnumerable<ITransitSource> sources) => _sources = sources;

    public ITransitSource? For(Feed feed) => _sources.FirstOrDefault(s => s.CanHandle(feed));
}
