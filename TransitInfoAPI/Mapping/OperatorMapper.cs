using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class OperatorMapper
{
    public static Expression<Func<Operator, OperatorResponse>> ToResponseExpression =>
        op => new OperatorResponse
        {
            Id = op.Id,
            GlobalId = op.GlobalId,
            OnestopId = op.OnestopId,
            Name = op.Name,
            ShortName = op.ShortName,
            Website = op.Website,
            CountryName = op.Country != null ? op.Country.Name : null
        };

    public static OperatorResponse ToResponse(Operator op) => new()
    {
        Id = op.Id,
        GlobalId = op.GlobalId,
        OnestopId = op.OnestopId,
        Name = op.Name,
        ShortName = op.ShortName,
        Website = op.Website,
        CountryName = op.Country?.Name
    };

    public static OperatorBriefResponse ToBriefResponse(Operator op) => new()
    {
        GlobalId = op.GlobalId,
        Name = op.Name,
        ShortName = op.ShortName
    };
}
