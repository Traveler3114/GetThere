using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GetThereAPI.Transit;

public class OtpClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OtpOptions _otp;

    public OtpClient(IHttpClientFactory httpFactory, IOptions<OtpOptions> otp)
    {
        _httpFactory = httpFactory;
        _otp = otp.Value;
    }

    public async Task<JsonDocument?> QueryAsync(
        string instanceKey,
        string query,
        object? variables = null,
        CancellationToken ct = default)
    {
        if (!_otp.Instances.TryGetValue(instanceKey, out var instance))
            return null;

        if (string.IsNullOrWhiteSpace(instance.BaseUrl))
            return null;

        var baseUrl = instance.BaseUrl.TrimEnd('/');
        var gqlPath = instance.GraphQlPath.StartsWith("/")
            ? instance.GraphQlPath
            : "/" + instance.GraphQlPath;

        var payload = new
        {
            query,
            variables
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + gqlPath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        try
        {
            using var http = _httpFactory.CreateClient();
            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var raw = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (raw.ValueKind != JsonValueKind.Object)
                return null;

            if (raw.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                return null;
            }

            var json = raw.GetRawText();
            return JsonDocument.Parse(json);
        }
        catch (HttpRequestException)
        {
            // API is down / refused connection
            return null;
        }
        catch (TaskCanceledException)
        {
            // timeout or cancellation
            return null;
        }
    }
}
