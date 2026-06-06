using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class OperatorMapper
{
    public static OperatorResponse ToResponse(TransitOperator entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        LogoUrl = entity.LogoUrl,
        City = entity.City?.Name,
        Country = entity.Country.Name,
    };

    public static TransportTypeResponse ToResponse(TransportType entity) => new()
    {
        GtfsRouteType = entity.GtfsRouteType,
        Name = entity.Name,
        IconFile = entity.IconFile,
        Color = entity.Color,
    };

    public static OperatorFeedResponse ToFeedResponse(TransitOperator entity) => new()
    {
        OperatorId = entity.Id,
        OperatorName = entity.Name,
        CountryId = entity.CountryId,
        CountryName = entity.Country.Name,
        FeedId = $"op{entity.Id}",
        StaticGtfsUrl = entity.GtfsFeedUrl,
        GtfsRealtimeUrl = entity.GtfsRealtimeFeedUrl,
    };
}
