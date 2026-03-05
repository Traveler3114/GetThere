using Microsoft.Extensions.Logging;
using System.Reflection;
using GetThere.Helpers;
using GetThere.Services;

namespace GetThere
{
    public static class MauiProgram
    {
        private const string ApiBaseUrl = "https://localhost:7230/";

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // AuthService gets a PLAIN HttpClient — no token attached
            // This makes sense because AuthService IS how you get the token in the first place
            // You can't attach a token to a login request!
            builder.Services.AddHttpClient("AuthService", client =>
            {
                client.BaseAddress = new Uri(ApiBaseUrl);
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
                    client.BaseAddress = new Uri(ApiBaseUrl);
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