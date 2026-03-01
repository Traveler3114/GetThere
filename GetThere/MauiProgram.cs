using Microsoft.Extensions.Logging;
using GetThere.Services;

namespace GetThere
{
    public static class MauiProgram
    {
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

            // Register HttpClient pointed at your API base URL
            builder.Services.AddHttpClient<AuthService>(client =>
            {
                // Use 10.0.2.2 for Android emulator (maps to localhost on your dev machine)
                // Use localhost:7230 for Windows/iOS
                client.BaseAddress = new Uri("https://localhost:7230/");
            });

            // Register pages for dependency injection
            builder.Services.AddTransient<Pages.LoginPage>();
            builder.Services.AddTransient<Pages.RegistrationPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}