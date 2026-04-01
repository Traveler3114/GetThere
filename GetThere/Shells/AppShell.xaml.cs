#nullable enable

namespace GetThere;

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

        profileTab.Icon = source ?? "icon_profile.svg";
    }
}
