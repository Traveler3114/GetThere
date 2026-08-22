namespace TransitInfoAPI.Contracts;

/// <summary>A canonical route (transit line) with operator and type info.</summary>
public class RouteResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }

    /// <summary>Slug of the feed whose last import saw this route
    /// (<c>CanonicalRoute.LastSeenFeedVersionId</c>). Null for a route no active import touches.
    /// This is "last seen in", not ownership — label it as such in the UI.</summary>
    public string? FeedId { get; set; }

    public string? OperatorGlobalId { get; set; }
}

public class RouteInfoResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public string? OperatorGlobalId { get; set; }
}
