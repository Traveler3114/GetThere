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
    private static readonly Lazy<string> ConfiguredBase = new(() => Resolve("Api", DefaultBaseUrl));
    private static readonly Lazy<string> ConfiguredMapBase = new(() => Resolve("Map", DefaultMapBaseUrl));

    /// <summary>GetThereAPI base address, always with a trailing slash.</summary>
    public static string GetThereApiBase => ConfiguredBase.Value;

    /// <summary>
    /// TransitInfoAPI base address, always with a trailing slash. Used for the map only.
    /// <para>
    /// The client reads this service directly rather than through GetThereAPI. The map page is
    /// served from here and calls this service same-origin as an ordinary anonymous browser, which
    /// is what removed the proxy, its allow-list and the bearer token this app used to have to
    /// inject into the WebView. Business data still goes exclusively through GetThereAPI.
    /// </para>
    /// </summary>
    public static string TransitInfoApiBase => ConfiguredMapBase.Value;

    /// <summary>
    /// Page hosting the map WebView, served by TransitInfoAPI.
    /// </summary>
    /// <param name="lang">
    /// Two-letter culture code for the page's own labels. The chrome used to be drawn natively from
    /// <c>AppResources</c>; the page owns it now, so the language has to travel with the URL.
    /// </param>
    public static string MapPageUrl(string lang) =>
        $"{TransitInfoApiBase}map/public.html?lang={Uri.EscapeDataString(lang)}";

    /// <summary>The address used when configuration supplies none.</summary>
    private static string DefaultBaseUrl =>
#if ANDROID
        "https://10.0.2.2:7230/";
#else
        "https://localhost:7230/";
#endif

    /// <summary>
    /// TransitInfoAPI's default address — its <c>https</c> launch profile, not the <c>http</c> one.
    /// <para>
    /// The manifest sets <c>usesCleartextTraffic="false"</c>, so the plain-HTTP profile on port 5000
    /// fails silently on Android. Run TransitInfoAPI with <c>--launch-profile https</c>.
    /// </para>
    /// </summary>
    private static string DefaultMapBaseUrl =>
#if ANDROID
        "https://10.0.2.2:5001/";
#else
        "https://localhost:5001/";
#endif

    /// <summary>
    /// Reads <c>{section}:BaseUrl</c> from the packaged settings file, falling back to the
    /// compile-time default when it is absent, blank or unreadable.
    /// </summary>
    private static string Resolve(string section, string fallback)
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());

            if (doc.RootElement.TryGetProperty(section, out var node) &&
                node.TryGetProperty("BaseUrl", out var baseUrl) &&
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
            Trace.WriteLine($"[ApiEndpoints] Could not read {section}:BaseUrl, using the default: {ex.Message}");
        }

        return fallback;
    }
}
