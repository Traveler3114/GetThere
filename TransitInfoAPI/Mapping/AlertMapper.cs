using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class AlertMapper
{
    public static AlertResponse ToResponse(Alert a) => new()
    {
        Id = a.Id,
        HeaderText = a.HeaderText,
        DescriptionText = a.DescriptionText,
        Url = a.Url,
        Cause = a.Cause,
        Effect = a.Effect,
        ActivePeriodStart = a.ActivePeriodStart,
        ActivePeriodEnd = a.ActivePeriodEnd,
        FetchedAt = a.FetchedAt
    };
}
