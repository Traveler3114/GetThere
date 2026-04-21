using Microsoft.Maui.Controls.Xaml;
using Microsoft.Extensions.DependencyInjection;
using GetThere.Services;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace GetThere
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var authService = _serviceProvider.GetService<AuthService>();
            if (authService?.GetRememberMe() == true)
            {
                var accessToken = SecureStorage.GetAsync("jwt_token").GetAwaiter().GetResult();
                var refreshToken = SecureStorage.GetAsync("refresh_token").GetAwaiter().GetResult();

                if (!string.IsNullOrWhiteSpace(accessToken) || !string.IsNullOrWhiteSpace(refreshToken))
                    return new Window(new AppShell());
            }

            return new Window(new LoginShell());
        }

        public static void GoToApp()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Current?.Windows.Count > 0)
                    Current.Windows[0].Page = new AppShell();
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

        public static async void GoToRegistration()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("registration");
            }
            else if (Current?.Windows.Count > 0)
            {
                var shell = new LoginShell();
                Current.Windows[0].Page = shell;
                await shell.GoToAsync("registration");
            }
        }
    }
}
