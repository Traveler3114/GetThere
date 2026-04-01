using System.Windows.Input;
using GetThere.Pages;

namespace GetThere
{
    public partial class LoginShell : Shell
    {
        public ICommand GoToLoginCommand { get; }
        public ICommand GoToRegisterCommand { get; }

        public LoginShell()
        {
            InitializeComponent();
            GoToLoginCommand = new Command(async () => await GoToAsync("///login"));
            GoToRegisterCommand = new Command(async () => await GoToAsync("registration"));
            Routing.RegisterRoute("registration", typeof(RegistrationPage));
            BindingContext = this;
        }
    }
}
