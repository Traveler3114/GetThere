using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Xaml;

using GetThere.Services;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace GetThere;
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        public static IServiceProvider Services => Current is App app ? app._serviceProvider : null!;

        public App(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new ContentPage());
            _ = InitializeWindowAsync(window);
            return window;
        }

        private async Task InitializeWindowAsync(Window window)
        {
            try
            {
                var authService = _serviceProvider.GetService<AuthService>();
                if (authService?.GetRememberMe() == true)
                {
                    var accessToken = await SecureStorage.GetAsync(AuthService.TokenKey);
                    var refreshToken = await SecureStorage.GetAsync(AuthService.RefreshTokenKey);

                    if (!string.IsNullOrWhiteSpace(accessToken) || !string.IsNullOrWhiteSpace(refreshToken))
                    {
                        window.Page = _serviceProvider.GetRequiredService<AppShell>();
                        return;
                    }
                }

                window.Page = AuthService.IsGuest() ? _serviceProvider.GetRequiredService<AppShell>() : new LoginShell();
            }
            catch
            {
                window.Page = new LoginShell(); // LoginShell has no DI deps
            }
        }

        public static void GoToApp()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Current?.Windows.Count > 0)
                    Current.Windows[0].Page = Services.GetRequiredService<AppShell>();
            });
        }

        public static void GoToLogin()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Current?.Windows.Count > 0)
                    Current.Windows[0].Page = new LoginShell();
            });
        }

    }
