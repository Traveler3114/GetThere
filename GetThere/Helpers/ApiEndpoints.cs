using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Helpers;

/// <summary>
/// Single source of truth for the backend addresses the app talks to.
/// <para>
/// These were literals compiled into the binary, so pointing a build at staging or production meant
/// editing source and rebuilding — and a build already handed to a tester could not be repointed at
/// all. The address now comes from the packaged <c>appsettings.json</c>, which is the same
/// <c>MauiAsset</c> the Sentry DSN is already read from.
/// </para>
/// <para>
/// The compile-time values remain as the fallback, so a build with no configured address behaves
/// exactly as before: the Android emulator reaches the host loopback through 10.0.2.2, everything
/// else uses localhost.
/// </para>
/// </summary>
public static class ApiEndpoints
{
    private static readonly Lazy<string> ConfiguredBase = new(ResolveBaseUrl);

    /// <summary>GetThereAPI base address, always with a trailing slash.</summary>
    public static string GetThereApiBase => ConfiguredBase.Value;

    /// <summary>
    /// Page hosting the map WebView. Served by GetThereAPI, and it calls GetThereAPI's own map
    /// proxy on the same origin — the client never reaches TransitInfoAPI directly, so the one-way
    /// rule in AGENTS.md holds. <c>MapPage</c> hands the page its bearer token after navigation.
    /// </summary>
    public static string MapPageUrl => $"{GetThereApiBase}map/public.html";

    /// <summary>The address used when configuration supplies none.</summary>
    private static string DefaultBaseUrl =>
#if ANDROID
        "https://10.0.2.2:7230/";
#else
        "https://localhost:7230/";
#endif

    private static string ResolveBaseUrl()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());

            if (doc.RootElement.TryGetProperty("Api", out var api) &&
                api.TryGetProperty("BaseUrl", out var baseUrl) &&
                baseUrl.GetString() is { Length: > 0 } configured)
            {
                // A missing trailing slash silently breaks every relative request: new
                // Uri("https://host/api", "wallet") resolves to https://host/wallet.
                return configured.EndsWith('/') ? configured : configured + "/";
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or FileNotFoundException)
        {
            // Falling back is correct here — an unreadable or malformed settings file should not
            // stop the app from starting against its default backend.
            Trace.WriteLine($"[ApiEndpoints] Could not read Api:BaseUrl, using the default: {ex.Message}");
        }

        return DefaultBaseUrl;
    }
}
