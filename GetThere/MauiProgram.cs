using System.Reflection;

using Microsoft.Extensions.Logging;

using CommunityToolkit.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;

using GetThere.Helpers;
using GetThere.Services;
using GetThere.State;

namespace GetThere;

public static class MauiProgram
{
    private static HttpMessageHandler CreateDevHttpHandler()
    {
        var handler = new HttpClientHandler();

#if DEBUG
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif

        return handler;
    }

    private static string GetApiBaseUrl()
    {
#if ANDROID
        return "https://10.0.2.2:7230/";
#elif IOS || MACCATALYST
        return "https://localhost:7230/";
#else
        return "https://localhost:7230/";
#endif
    }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        });
#endif

        var apiBase = GetApiBaseUrl();

        builder.Services.AddSingleton<CountryPreferenceService>();

        builder.Services.AddTransient<AuthService>(_ =>
            new AuthService(new HttpClient(CreateDevHttpHandler()) { BaseAddress = new Uri(apiBase) }));

        builder.Services.AddTransient<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient("GetThereAPI", client =>
            client.BaseAddress = new Uri(apiBase))
            .ConfigurePrimaryHttpMessageHandler(CreateDevHttpHandler)
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
            return new OperatorService(clientFactory.CreateClient("GetThereAPI"));
        });

        var assembly = Assembly.GetExecutingAssembly();

        var pageTypes = assembly
            .GetTypes()
            .Where(t => t.Namespace == "GetThere.Pages"
                     && t.IsClass
                     && !t.IsAbstract
                     && t.IsSubclassOf(typeof(ContentPage)));

        foreach (var pageType in pageTypes)
        {
            builder.Services.AddTransient(pageType);
        }

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<LoginShell>();

        return builder.Build();
    }
}
