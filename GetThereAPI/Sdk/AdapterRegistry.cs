namespace GetThereAPI.Sdk;

public class AdapterRegistry
{
    private readonly Dictionary<string, ITicketingAdapter> _adapters = new();

    public void Register(string adapterType, ITicketingAdapter adapter)
    {
        _adapters[adapterType] = adapter;
    }

    public ITicketingAdapter? Get(string adapterType)
    {
        _adapters.TryGetValue(adapterType, out var adapter);
        return adapter;
    }

    public IReadOnlyCollection<ITicketingAdapter> GetAll() => _adapters.Values;

    public bool HasAdapter(string adapterType) => _adapters.ContainsKey(adapterType);
}
