namespace OpenTripPlannerAPI.Scrapers.HZPP.Services;

/// <summary>
/// Simple signal so Program.cs knows when the scraper has finished
/// loading GTFS data and is ready, before OTP is launched.
/// </summary>
public static class GtfsReadySignal
{
    private static readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void SetReady() => _tcs.TrySetResult();

    public static Task WaitAsync() => _tcs.Task;
}
