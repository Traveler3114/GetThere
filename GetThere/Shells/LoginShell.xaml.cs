using GetThere.Pages;

namespace GetThere
{
    public partial class LoginShell : Shell
    {
        public LoginShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("registration", typeof(RegistrationPage));
        }
    }
}