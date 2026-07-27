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
    /// Page hosting the map WebView. Served by GetThereAPI, and it now calls GetThereAPI's own map
    /// proxy on the same origin — the client no longer reaches TransitInfoAPI directly, so the
    /// one-way rule in AGENTS.md holds. <c>MapPage</c> hands the page its bearer token after
    /// navigation.
    /// </summary>
    public static string MapPageUrl => $"{GetThereApiBase}map/public.html";
}
