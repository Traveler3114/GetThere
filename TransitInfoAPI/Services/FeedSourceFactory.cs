using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

public class FeedSourceFactory
{
    private readonly ExternalFeedSource _external;
    private readonly CustomFeedSource _custom;

    public FeedSourceFactory(ExternalFeedSource external, CustomFeedSource custom)
    {
        _external = external;
        _custom = custom;
    }

    public IFeedSource Resolve(Feed feed)
    {
        return feed.CustomFeedId is not null ? _custom : _external;
    }
}
