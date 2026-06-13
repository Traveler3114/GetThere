namespace TransitInfoAPI.Services.Converters;

public abstract class FeedConverterBase : IFeedConverter
{
    protected ILogger Logger { get; }
    protected IHttpClientFactory HttpClientFactory { get; }

    protected FeedConverterBase(ILogger logger, IHttpClientFactory httpClientFactory)
    {
        Logger = logger;
        HttpClientFactory = httpClientFactory;
    }

    public abstract string ConverterType { get; }
    public abstract Task ConvertAsync(Entities.FeedConverter config, CancellationToken ct);

    protected string GetOutputPath(Entities.FeedConverter config)
    {
        return Path.Combine(AppContext.BaseDirectory, "feeds", config.Feed.FeedId, $"{config.ConverterType}.pb");
    }
}
