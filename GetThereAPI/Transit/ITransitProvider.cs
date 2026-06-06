using GetThereShared.Contracts;

namespace GetThereAPI.Transit;

public interface ITransitProvider
{
    Task<List<StopResponse>> GetStopsAsync(string instanceKey, CancellationToken ct = default);
    Task<List<RouteResponse>> GetRoutesAsync(string instanceKey, CancellationToken ct = default);
    Task<StopScheduleResponse?> GetStopScheduleAsync(
        string instanceKey,
        string stopId,
        CancellationToken ct = default);
    Task<bool> HealthCheckAsync(string instanceKey, CancellationToken ct = default);

    // FUTURE (route planning):
    // - point-to-point transit itineraries for AI scoring
    // - multimodal handoff legs (OTP -> Flights -> OTP)
    // Task<object?> PlanRouteAsync(...);
}
