using Microsoft.Extensions.Logging;
using System.Reflection;
using GetThere.Helpers;
using GetThere.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui.Controls.Hosting;
using CommunityToolkit.Maui;
using System.Net.Http.Json;

namespace GetThere
{
    public static class MauiProgram
    {
        private static string GetApiBaseUrl()
        {
#if ANDROID
            // default Android emulator
            return "https://10.0.2.2:7230/";
#elif IOS || MACCATALYST
            // iOS simulator and Mac Catalyst can hit localhost directly
            return "https://localhost:7230/";
#else
            // Windows, desktop builds, etc.
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

            var baseUrl = GetApiBaseUrl();

            // ── SSL CERTIFICATE BYPASS (Dev only) ──
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

            // AuthService
            builder.Services.AddHttpClient("AuthService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            }).ConfigurePrimaryHttpMessageHandler(() => handler);

            builder.Services.AddTransient<AuthService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new AuthService(factory.CreateClient("AuthService"));
            });

            // Authenticated Handler
            builder.Services.AddTransient<AuthenticatedHttpHandler>();

            var assembly = Assembly.GetExecutingAssembly();

            // All other services
            var serviceTypes = assembly
                .GetTypes()
                .Where(t => t.Namespace == "GetThere.Services"
                         && t.IsClass
                         && !t.IsAbstract
                         && t != typeof(AuthService)
                         && t != typeof(AuthenticatedHttpHandler));

            foreach (var serviceType in serviceTypes)
            {
                builder.Services.AddHttpClient(serviceType.Name, client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
                })
                .AddHttpMessageHandler<AuthenticatedHttpHandler>()
                .ConfigurePrimaryHttpMessageHandler(() => handler);

                builder.Services.AddTransient(serviceType, sp =>
                {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(serviceType.Name);
                    return Activator.CreateInstance(serviceType, httpClient)!;
                });
            }

            // Pages
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

            // Shells
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<LoginShell>();

            return builder.Build();
        }
    }
}