using System.Text.Json;

namespace GetThereShared.Common;

public static class HttpHelper
{
    /// <summary>
    /// Reads the <c>title</c> field out of an RFC 9457 problem response, or null when the body is
    /// not problem JSON. Callers use the result to show a server-supplied message, so a body that
    /// cannot be parsed is an expected outcome rather than an error — but it is narrowed to parse
    /// failures so a cancellation or a broken connection still propagates.
    /// </summary>
    public static async Task<string?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }

        return null;
    }
}
