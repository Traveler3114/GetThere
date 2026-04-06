using GetThereShared.Dtos;

namespace GetThereAPI.Adapters;

public class NoopStationScheduleAdapter : IStationScheduleAdapter
{
    public Task<(List<DepartureGroupDto> Groups, string Status, string? Message)> GetStationScheduleAsync(
        string stationKey,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<(List<DepartureGroupDto>, string, string?)>(
            ([], "not_configured", "Station API adapter is not configured for this station."));
    }
}
