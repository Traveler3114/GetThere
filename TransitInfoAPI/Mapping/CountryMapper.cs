using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class CountryMapper
{
    public static Expression<Func<Country, CountryResponse>> ToResponseExpression =>
        c => new CountryResponse
        {
            Id = c.Id,
            Name = c.Name,
            IsoCode = c.IsoCode,
            Continent = c.Continent
        };

    public static CountryResponse ToResponse(Country c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        IsoCode = c.IsoCode,
        Continent = c.Continent
    };
}
