using GetThereAPI.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace GetThereAPI.Parsers.Mobility;

public class MobilityParserFactory
{
    private readonly IServiceProvider _services;

    public MobilityParserFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IMobilityParser GetParser(MobilityProvider provider)
        => _services.GetKeyedService<IMobilityParser>(provider.FeedFormat)
           ?? _services.GetKeyedService<IMobilityParser>(MobilityFeedFormat.NEXTBIKE_API)
           ?? throw new InvalidOperationException("No mobility parser is registered.");
}
