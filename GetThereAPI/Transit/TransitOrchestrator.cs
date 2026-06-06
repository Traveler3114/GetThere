using GetThereShared.Contracts;

namespace GetThereAPI.Transit;

public class TransitOrchestrator
{
    private readonly ITransitRouter _router;
    private readonly ITransitProvider _provider;

    public TransitOrchestrator(ITransitRouter router, ITransitProvider provider)
    {
        _router = router;
        _provider = provider;
    }

    public async Task<List<StopResponse>> GetStopsAsync(int? countryId, CancellationToken ct = default)
    {
        var instanceKey = await _router.ResolveInstanceKeyAsync(countryId, ct);
        return await _provider.GetStopsAsync(instanceKey, ct);
    }

    public async Task<List<RouteResponse>> GetRoutesAsync(int? countryId, CancellationToken ct = default)
    {
        var instanceKey = await _router.ResolveInstanceKeyAsync(countryId, ct);
        return await _provider.GetRoutesAsync(instanceKey, ct);
    }

    public async Task<StopScheduleResponse?> GetStopScheduleAsync(
        int? countryId,
        string stopId,
        CancellationToken ct = default)
    {
        var instanceKey = await _router.ResolveInstanceKeyAsync(countryId, ct);
        return await _provider.GetStopScheduleAsync(instanceKey, stopId, ct);
    }

    public async Task<bool> HealthCheckAsync(int? countryId, CancellationToken ct = default)
    {
        var instanceKey = await _router.ResolveInstanceKeyAsync(countryId, ct);
        return await _provider.HealthCheckAsync(instanceKey, ct);
    }
}
