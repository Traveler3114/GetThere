using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class CountryMapper
{
    public static CountryResponse ToResponse(Country entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
    };
}
