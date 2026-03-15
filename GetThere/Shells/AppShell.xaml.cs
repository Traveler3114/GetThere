#nullable enable
using System.Windows.Input;

namespace GetThere
{
    public partial class AppShell : Shell
    {
        public ICommand GoToProfileCommand { get; }
        public ICommand GoToMapCommand { get; }
        public ICommand GoToOperatorsCommand { get; }

        public AppShell()
        {
            InitializeComponent();

            GoToProfileCommand = new Command(async () => await GoToAsync("///profile"));
            GoToMapCommand = new Command(async () => await GoToAsync("///map"));
            GoToOperatorsCommand = new Command(async () => await GoToAsync("///operators"));

            BindingContext = this;
        }

        public void UpdateProfileIcon(ImageSource? source)
        {
            var profileTab = this.FindByName<ShellContent>("ProfileTab");
            if (profileTab == null) return;

            if (source != null)
            {
                profileTab.Icon = source;
            }
            else
            {
                profileTab.Icon = "icon_profile.svg";
            }
        }
    }
}