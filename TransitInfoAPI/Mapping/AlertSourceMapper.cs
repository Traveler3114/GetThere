using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class AlertSourceMapper
{
    public static AlertSourceResponse ToResponse(Feed feed) => new()
    {
        Id = feed.AlertSource!.Id,
        FeedId = feed.Id,
        FeedSlug = feed.FeedId,
        OperatorId = feed.OperatorId,
        OperatorName = feed.Operator?.Name ?? string.Empty,
        IsActive = feed.IsActive,
        SourceKey = feed.AlertSource.SourceKey,
        Kind = feed.AlertSource.Kind,
        Format = feed.AlertSource.Format,
        Url = feed.AlertSource.Url,
        ItemSelector = feed.AlertSource.ItemSelector,
        TitleSelector = feed.AlertSource.TitleSelector,
        DescriptionSelector = feed.AlertSource.DescriptionSelector,
        DateSelector = feed.AlertSource.DateSelector,
        LinkSelector = feed.AlertSource.LinkSelector,
        CategorySelector = feed.AlertSource.CategorySelector,
        IntervalMinutes = feed.AlertSource.IntervalMinutes,
        LastRunAt = feed.AlertSource.LastRunAt,
        LastItemCount = feed.AlertSource.LastItemCount,
        LastError = feed.AlertSource.LastError
    };
}
