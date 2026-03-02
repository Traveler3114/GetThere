using Microsoft.Extensions.Logging;
using System.Reflection;

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

            var assembly = Assembly.GetExecutingAssembly();

            // Auto-register all services in GetThere.Services namespace
            var serviceTypes = assembly
                .GetTypes()
                .Where(t => t.Namespace == "GetThere.Services" && t.IsClass && !t.IsAbstract);

            foreach (var serviceType in serviceTypes)
            {
                builder.Services.AddHttpClient(serviceType.Name, client =>
                {
                    client.BaseAddress = new Uri(ApiBaseUrl);
                });
                builder.Services.AddTransient(serviceType, sp =>
                {
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient(serviceType.Name);
                    return Activator.CreateInstance(serviceType, httpClient)!;
                });
            }

            // Auto-register all pages in GetThere.Pages namespace
            var pageTypes = assembly
                .GetTypes()
                .Where(t => t.Namespace == "GetThere.Pages" && t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ContentPage)));

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