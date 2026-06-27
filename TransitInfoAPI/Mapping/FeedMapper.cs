using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class FeedMapper
{
    public static Expression<Func<Feed, FeedResponse>> ToResponseExpression =>
        f => new FeedResponse
        {
            Id = f.Id,
            OnestopId = f.OnestopId,
            FeedType = f.FeedType.ToString(),
            FeedId = f.FeedId,
            ExternalUrl = f.ExternalUrl,
            InternalUrl = f.InternalUrl,
            IsActive = f.IsActive,
            IsInternal = f.IsInternal,
            CustomFeedId = f.CustomFeedId,
            RefreshIntervalSeconds = f.RefreshIntervalSeconds,
            OperatorName = f.Operator != null ? f.Operator.Name : null,
            LicenseName = f.LicenseName,
            LicenseUrl = f.LicenseUrl,
            LicenseCommercialUseAllowed = f.LicenseCommercialUseAllowed,
            LicenseShareAlikeOptional = f.LicenseShareAlikeOptional,
            LicenseRedistributionAllowed = f.LicenseRedistributionAllowed
        };

    public static FeedResponse ToResponse(Feed f) => new()
    {
        Id = f.Id,
        OnestopId = f.OnestopId,
        FeedType = f.FeedType.ToString(),
        FeedId = f.FeedId,
        ExternalUrl = f.ExternalUrl,
        InternalUrl = f.InternalUrl,
        IsActive = f.IsActive,
        IsInternal = f.IsInternal,
        CustomFeedId = f.CustomFeedId,
        RefreshIntervalSeconds = f.RefreshIntervalSeconds,
        OperatorName = f.Operator?.Name,
        LicenseName = f.LicenseName,
        LicenseUrl = f.LicenseUrl,
        LicenseCommercialUseAllowed = f.LicenseCommercialUseAllowed,
        LicenseShareAlikeOptional = f.LicenseShareAlikeOptional,
        LicenseRedistributionAllowed = f.LicenseRedistributionAllowed
    };
}
