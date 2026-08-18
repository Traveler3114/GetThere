using System.Threading.Channels;

namespace TransitInfoAPI.Routing;

/// <summary>
/// A fire-and-forget request that the routing artifacts (and, in deployment, the OTP graph) be
/// rebuilt. Decouples the import pipeline from the exporter: <c>FeedManager</c> depends only on this
/// small interface and calls it after a feed version activates, rather than running an export inline
/// and blocking the import.
/// </summary>
public interface IGraphRebuildSignal
{
    /// <summary>Requests a rebuild. Coalesces: many activations in quick succession collapse to one.</summary>
    void Request(string reason);
}

/// <summary>
/// Channel-backed implementation. The bounded-to-one, drop-write channel is what coalesces a burst of
/// activations (e.g. several feeds refreshing together) into a single rebuild instead of one per feed.
/// </summary>
public sealed class GraphRebuildSignal : IGraphRebuildSignal
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Request(string reason) => _channel.Writer.TryWrite(reason);

    /// <summary>Consumed by the rebuild worker; yields once per coalesced batch of requests.</summary>
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
