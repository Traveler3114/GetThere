namespace GetThereAPI.Sdk;

public abstract class AdapterBase : ITicketingAdapter
{
    protected readonly HttpClient _http;
    protected readonly string _baseUrl;
    protected readonly string? _apiKey;

    protected AdapterBase(HttpClient http, string baseUrl, string? apiKey)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public abstract string Name { get; }
    public abstract string AdapterType { get; }
    public abstract List<Models.RequiredInput> RequiredInputs { get; }
    public abstract Task<Models.PurchaseResult> PurchaseAsync(Models.PurchaseRequest request, CancellationToken ct = default);
    public abstract Task<Models.TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default);

    protected HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);
        return request;
    }
}
