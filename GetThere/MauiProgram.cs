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

    private static string? LoadSentryDsn()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("Sentry").GetProperty("Dsn").GetString();
        }
        catch
        {
            return null;
        }
    }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSentry(options =>
            {
                options.Dsn = LoadSentryDsn() ?? "";
                options.Debug = false;
                options.TracesSampleRate = 0.0;
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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<LoginShell>();

        return builder.Build();
    }
}
