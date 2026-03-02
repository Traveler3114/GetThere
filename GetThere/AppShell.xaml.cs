namespace GetThere
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("registration", typeof(Pages.RegistrationPage));
            Routing.RegisterRoute("map", typeof(Pages.MapPage));
        }
    }
}