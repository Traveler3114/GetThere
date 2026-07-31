using System.Reflection;
using System.Text.Json;

using CommunityToolkit.Maui;

using GetThere.Helpers;
using GetThere.Services;
using GetThere.State;
using GetThere.ViewModels;

using Microsoft.Extensions.Logging;

using SkiaSharp.Views.Maui.Controls.Hosting;

namespace GetThere;

public static class MauiProgram
{
    private static string GetApiBaseUrl() => Helpers.ApiEndpoints.GetThereApiBase;

    /// <summary>
    /// Reads the crash-reporting DSN out of the packaged settings file.
    /// <para>
    /// Blocking on the async read is forced: <see cref="CreateMauiApp"/> is synchronous by contract
    /// and the DSN has to be known before <c>UseSentry</c> is configured. It runs once, at startup,
    /// before there is a message loop to deadlock against.
    /// </para>
    /// </summary>
    private static string? LoadSentryDsn()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);

            // TryGetProperty rather than GetProperty: a settings file without a Sentry section is a
            // legitimate configuration, not an error, and this used to reach the catch below and be
            // indistinguishable from a genuinely unreadable file.
            return doc.RootElement.TryGetProperty("Sentry", out var sentry)
                && sentry.TryGetProperty("Dsn", out var dsn)
                    ? dsn.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FileNotFoundException)
        {
            // Narrowed from a bare catch. Crash reporting staying off because the settings file
            // could not be read is survivable; swallowing every exception type here hid unrelated
            // startup faults behind "no DSN".
            System.Diagnostics.Trace.WriteLine($"[MauiProgram] Could not read Sentry:Dsn, crash reporting stays off: {ex.Message}");
            return null;
        }
    }

    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        // Strips the WinUI border/background the design system draws itself. See
        // Platforms/Windows/WindowsControlStyling.cs — without it every field renders a square
        // TextBox inside the rounded DsField border.
        WindowsControlStyling.Apply();
#endif

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // Inert while the DSN is blank, which is how it ships: the committed appsettings.json
            // carries an empty value, so crash reporting does nothing until a real DSN is supplied.
            // Set one before any build reaches a tester, or field reports arrive with nothing
            // attached.
            .UseSentry(options =>
            {
                options.Dsn = LoadSentryDsn() ?? "";
                options.Debug = false;
                options.TracesSampleRate = 0.0;

                // Warnings and above become breadcrumbs on whatever crash or captured exception
                // follows, which is the part that makes a report actionable. Errors are captured as
                // events in their own right.
                options.MinimumBreadcrumbLevel = LogLevel.Warning;
                options.MinimumEventLevel = LogLevel.Error;
            })
            .UseSkiaSharp()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var apiBase = GetApiBaseUrl();

        builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();
        builder.Services.AddSingleton<CountryPreferenceService>();

        // Singleton: AuthService holds the in-memory token cache and serializes token refresh.
        // As a transient, every consumer got its own cache (so the cache never hit) and its own
        // refresh lock (so concurrent requests each rotated the refresh token, and the loser was
        // logged out). It also allocates its own HttpClient, which must not be per-instance.
        builder.Services.AddSingleton<AuthService>(_ =>
            new AuthService(new HttpClient { BaseAddress = new Uri(apiBase) }));

        builder.Services.AddTransient<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient("GetThereAPI", client =>
        {
            client.BaseAddress = new Uri(apiBase);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        builder.Services.AddTransient(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new WalletService(clientFactory.CreateClient("GetThereAPI"));
        });

        builder.Services.AddTransient(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new TicketService(clientFactory.CreateClient("GetThereAPI"));
        });

        builder.Services.AddTransient(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new CountryService(clientFactory.CreateClient("GetThereAPI"));
        });

        builder.Services.AddTransient(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new ImportedTicketService(clientFactory.CreateClient("GetThereAPI"));
        });

        builder.Services.AddTransient(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new JourneyService(clientFactory.CreateClient("GetThereAPI"));
        });

        // Stateless: it wraps the platform pickers and a Skia re-encode, holding nothing between
        // calls, so one instance serves every import.
        builder.Services.AddSingleton<TicketCaptureService>();

        // Also stateless — payload in, image out, no cache and no HttpClient.
        builder.Services.AddSingleton<BarcodeRenderService>();

        // Singleton because it owns a write lock: two screens finishing a load at the same moment
        // must not interleave into a half-written file.
        builder.Services.AddSingleton<TicketStore>();

        var assembly = Assembly.GetExecutingAssembly();

        var pageTypes = assembly
            .GetTypes()
            .Where(t => t.Namespace == "GetThere.Pages"
                     && t.IsClass
                     && !t.IsAbstract
                     && t.IsSubclassOf(typeof(ContentPage)));

        foreach (var pageType in pageTypes)
            builder.Services.AddTransient(pageType);

        var viewModelTypes = assembly
            .GetTypes()
            .Where(t => t.Namespace == "GetThere.ViewModels"
                     && t.IsClass
                     && !t.IsAbstract
                     && t.IsSubclassOf(typeof(BaseViewModel)));

        foreach (var vmType in viewModelTypes)
            builder.Services.AddTransient(vmType);

        // Debug-only, and deliberately: outside a debugger this sink goes nowhere. Release logging
        // reaches Sentry instead — UseSentry above registers the ILogger integration itself, so
        // adding a second provider here would only duplicate every breadcrumb.
#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<LoginShell>();

        return builder.Build();
    }
}
