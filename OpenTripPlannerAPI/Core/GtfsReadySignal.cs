using System.Collections.Concurrent;

namespace OpenTripPlannerAPI.Core;

public sealed class GtfsReadySignal
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals = new(StringComparer.OrdinalIgnoreCase);

    public void SetReady(string feedId)
    {
        var signal = _signals.GetOrAdd(
            feedId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        signal.TrySetResult();
    }

    public Task WaitAsync(string feedId, CancellationToken ct = default)
    {
        var signal = _signals.GetOrAdd(
            feedId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        return signal.Task.WaitAsync(ct);
    }
}
