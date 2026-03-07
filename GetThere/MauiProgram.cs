using Microsoft.Extensions.Logging;
using System.Reflection;
using GetThere.Helpers;
using GetThere.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;
using CommunityToolkit.Maui;

namespace GetThere
{
    public static class MauiProgram
    {
        // the base address we want to hit. localhost works when the app is
        // running on the same machine (Windows or the iOS simulator), but
        // Android emulators and physical devices cannot see the host's
        // loopback interface. 10.0.2.2 is the special alias for the host on
        // the default Android emulator.  When you deploy to a real device
        // you'll need to use the machine's LAN IP or a tunnel instead.
        private static string GetApiBaseUrl()
        {
#if ANDROID
            // default Android emulator
            return "http://10.0.2.2:5000/";
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

            // AuthService gets a PLAIN HttpClient — no token attached
            // This makes sense because AuthService IS how you get the token in the first place
            // You can't attach a token to a login request!
            var baseUrl = GetApiBaseUrl();
            builder.Services.AddHttpClient("AuthService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            });
            builder.Services.AddTransient<AuthService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new AuthService(factory.CreateClient("AuthService"));
            });

            // Register the handler so DI knows about it
            builder.Services.AddTransient<AuthenticatedHttpHandler>();

            var assembly = Assembly.GetExecutingAssembly();

            // All other services (TicketService, WalletService, etc.) get the
            // authenticated handler automatically — so every request they make
            // will have the JWT attached without any extra work
            var serviceTypes = assembly
                .GetTypes()
                .Where(t => t.Namespace == "GetThere.Services"
                         && t.IsClass
                         && !t.IsAbstract
                         && t != typeof(AuthService)              // already registered above
                         && t != typeof(AuthenticatedHttpHandler)); // not a service itself

            foreach (var serviceType in serviceTypes)
            {
                builder.Services.AddHttpClient(serviceType.Name, client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
                })
                .AddHttpMessageHandler<AuthenticatedHttpHandler>(); // <-- attach the handler

                builder.Services.AddTransient(serviceType, sp =>
                {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(serviceType.Name);
                    return Activator.CreateInstance(serviceType, httpClient)!;
                });
            }

            // Auto-register pages — unchanged
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

            return builder.Build();
        }
    }
}