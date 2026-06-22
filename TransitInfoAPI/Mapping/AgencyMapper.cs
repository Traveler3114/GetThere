using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class AgencyMapper
{
    public static Expression<Func<Agency, AgencyResponse>> ToResponseExpression =>
        a => new AgencyResponse
        {
            Id = a.Id,
            AgencyId = a.AgencyId,
            Name = a.Name,
            Url = a.Url,
            Timezone = a.Timezone,
            Phone = a.Phone,
            OperatorId = a.OperatorId,
            OperatorName = a.Operator != null ? a.Operator.Name : null,
            FeedVersionId = a.FeedVersionId
        };

    public static AgencyResponse ToResponse(Agency a) => new()
    {
        Id = a.Id,
        AgencyId = a.AgencyId,
        Name = a.Name,
        Url = a.Url,
        Timezone = a.Timezone,
        Phone = a.Phone,
        OperatorId = a.OperatorId,
        OperatorName = a.Operator?.Name,
        FeedVersionId = a.FeedVersionId
    };
}
