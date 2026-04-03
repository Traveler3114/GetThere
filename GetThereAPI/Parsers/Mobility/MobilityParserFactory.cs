using GetThereAPI.Entities;

namespace GetThereAPI.Parsers.Mobility;

public static class MobilityParserFactory
{
    private static readonly NextbikeParser _nextbike = new();

    public static IMobilityParser GetParser(MobilityProvider provider)
        => provider.FeedFormat switch
        {
            MobilityFeedFormat.NEXTBIKE_API => _nextbike,
            MobilityFeedFormat.GBFS         => _nextbike, // placeholder until GbfsParser is added
            MobilityFeedFormat.BOLT_API     => _nextbike, // placeholder until BoltParser is added
            MobilityFeedFormat.REST         => _nextbike, // placeholder until RestMobilityParser is added
            _                               => _nextbike
        };
}
