#nullable enable
namespace GetThere
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new LoginShell());
        }

        public static void GoToApp()
        {
            if (Current?.Windows.Count > 0)
                Current.Windows[0].Page = new AppShell();
        }

        public static void GoToLogin()
        {
            if (Current?.Windows.Count > 0)
                Current.Windows[0].Page = new LoginShell();
        }

        public static async void GoToRegistration()
        {
            if (Current?.Windows.Count > 0)
            {
                var shell = new LoginShell();
                Current.Windows[0].Page = shell;
                await shell.GoToAsync("//registration");
            }
        }
    }
}