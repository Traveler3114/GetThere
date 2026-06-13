namespace TransitInfoAPI.Services.Converters;

public interface IFeedConverter
{
    string ConverterType { get; }
    Task ConvertAsync(Entities.FeedConverter config, CancellationToken ct);
}
