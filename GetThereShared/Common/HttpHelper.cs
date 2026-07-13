using System.Text.Json;

namespace GetThereShared.Common;

public static class HttpHelper
{
    public static async Task<string?> TryReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch { }
        return null;
    }
}