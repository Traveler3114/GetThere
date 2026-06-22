using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class RouteMapper
{
    public static Expression<Func<CanonicalRoute, RouteResponse>> ToResponseExpression =>
        r => new RouteResponse
        {
            Id = r.Id,
            GlobalId = r.GlobalId,
            OnestopId = r.OnestopId,
            Name = r.LongName,
            ShortName = r.ShortName,
            RouteType = r.RouteType.ToString(),
            OperatorId = r.OperatorId,
            OperatorName = r.Operator != null ? r.Operator.Name : null
        };

    public static RouteResponse ToResponse(CanonicalRoute r) => new()
    {
        Id = r.Id,
        GlobalId = r.GlobalId,
        OnestopId = r.OnestopId,
        Name = r.LongName,
        ShortName = r.ShortName,
        RouteType = r.RouteType.ToString(),
        OperatorId = r.OperatorId,
        OperatorName = r.Operator?.Name
    };

    public static RouteInfoResponse ToInfoResponse(CanonicalRoute r) => new()
    {
        Id = r.Id,
        Name = r.LongName,
        ShortName = r.ShortName,
        RouteType = r.RouteType.ToString(),
        OperatorName = r.Operator?.Name,
        OperatorGlobalId = r.Operator?.GlobalId
    };
}
