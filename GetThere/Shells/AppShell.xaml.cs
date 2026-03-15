namespace GetThere
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
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