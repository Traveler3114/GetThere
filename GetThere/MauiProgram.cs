using Microsoft.Extensions.Logging;
using System.Reflection;
using GetThere.Helpers;
using GetThere.Services;
using GetThere.State;
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

            var baseUrl = GetApiBaseUrl();

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

            builder.Services.AddHttpClient("AuthService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            }).ConfigurePrimaryHttpMessageHandler(() => handler);

            builder.Services.AddTransient<AuthService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new AuthService(factory.CreateClient("AuthService"));
            });

            builder.Services.AddTransient<AuthenticatedHttpHandler>();

            builder.Services.AddHttpClient("CountryService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.AddTransient<CountryService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new CountryService(factory.CreateClient("CountryService"));
            });

            builder.Services.AddHttpClient("OperatorService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.AddTransient<OperatorService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new OperatorService(factory.CreateClient("OperatorService"));
            });

            builder.Services.AddHttpClient("PaymentService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.AddTransient<PaymentService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new PaymentService(factory.CreateClient("PaymentService"));
            });

            builder.Services.AddHttpClient("ShopService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.AddTransient<ShopService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new ShopService(factory.CreateClient("ShopService"));
            });

            builder.Services.AddHttpClient("TicketService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.AddTransient<TicketService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new TicketService(factory.CreateClient("TicketService"));
            });

            builder.Services.AddHttpClient("WalletService", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.AddTransient<WalletService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new WalletService(factory.CreateClient("WalletService"));
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

            builder.Services.AddSingleton<CountryPreferenceService>();
            builder.Services.AddSingleton<MockTicketStore>();

            return builder.Build();
        }
    }
}
