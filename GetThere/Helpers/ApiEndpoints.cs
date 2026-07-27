namespace GetThere.Helpers;

/// <summary>
/// Single source of truth for the backend addresses the app talks to.
/// <para>
/// These were previously duplicated as literals in <c>MauiProgram</c> and <c>MapViewModel</c>, which
/// meant a deployment target could only be changed by editing two files and rebuilding. They are
/// still compile-time values (a released build cannot be repointed), but there is now one place to
/// change and one place to replace when the addresses move into configuration.
/// </para>
/// </summary>
public static class ApiEndpoints
{
    /// <summary>
    /// GetThereAPI base address. The Android emulator reaches the host loopback through 10.0.2.2.
    /// </summary>
    public static string GetThereApiBase =>
#if ANDROID
        "https://10.0.2.2:7230/";
#else
        "https://localhost:7230/";
#endif

    /// <summary>
    /// TransitInfoAPI base address.
    /// <para>
    /// KNOWN ARCHITECTURE VIOLATION: the MAUI client is supposed to talk only to GetThereAPI, but
    /// the map page fetches GeoJSON straight from TransitInfoAPI. Closing this needs GeoJSON
    /// passthrough on <c>MapProxyController</c> and a way to hand the WebView a bearer token —
    /// see docs/map-proxy-migration.md. Kept here so the violation lives in exactly one place.
    /// </para>
    /// </summary>
    public static string TransitInfoApiBase =>
#if ANDROID
        "https://10.0.2.2:5001";
#else
        "https://localhost:5001";
#endif

    /// <summary>
    /// Page hosting the map WebView. Served by GetThereAPI; the <c>api</c> parameter tells the page
    /// which backend to query — see the note on <see cref="TransitInfoApiBase"/>.
    /// </summary>
    public static string MapPageUrl => $"{GetThereApiBase}map/public.html?api={TransitInfoApiBase}";
}
