using GetThereShared.Dtos;

namespace GetThereAPI.Transit;

public interface ITransitProvider
{
    Task<List<StopDto>> GetStopsAsync(string instanceKey, CancellationToken ct = default);
    Task<List<RouteDto>> GetRoutesAsync(string instanceKey, CancellationToken ct = default);
    Task<StopScheduleDto?> GetStopScheduleAsync(
        string instanceKey,
        string stopId,
        CancellationToken ct = default);
    Task<bool> HealthCheckAsync(string instanceKey, CancellationToken ct = default);

    // FUTURE (route planning):
    // - point-to-point transit itineraries for AI scoring
    // - multimodal handoff legs (OTP -> Flights -> OTP)
    // Task<object?> PlanRouteAsync(...);
}
