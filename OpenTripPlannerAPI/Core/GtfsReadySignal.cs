namespace OpenTripPlannerAPI.Core;

public sealed class GtfsReadySignal
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetReady() => _tcs.TrySetResult();

    public Task WaitAsync() => _tcs.Task;
}
