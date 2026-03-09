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
            Current!.Windows[0].Page = new AppShell();
        }

        public static void GoToLogin()
        {
            Current!.Windows[0].Page = new LoginShell();
        }

        public static async void GoToRegistration()
        {
            Current!.Windows[0].Page = new LoginShell();
            await Shell.Current.GoToAsync("//registration");
        }
    }
}