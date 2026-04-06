using GetThereShared.Dtos;

namespace GetThereAPI.Adapters;

public interface IStationScheduleAdapter
{
    Task<(List<DepartureGroupDto> Groups, string Status, string? Message)> GetStationScheduleAsync(
        string stationKey,
        DateOnly date,
        CancellationToken cancellationToken = default);
}
