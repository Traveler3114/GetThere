using TransitInfoAPI.Services.Converters;

namespace TransitInfoAPI.Services;

public class ConverterRegistry
{
    private readonly Dictionary<string, IFeedConverter> _converters = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IFeedConverter converter)
    {
        _converters[converter.ConverterType] = converter;
    }

    public IFeedConverter? Get(string converterType)
    {
        return _converters.GetValueOrDefault(converterType);
    }

    public IReadOnlyCollection<IFeedConverter> GetAll() => _converters.Values;
}
