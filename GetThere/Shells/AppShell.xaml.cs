using Microsoft.Maui.Controls;

using GetThere.State;

namespace GetThere;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
    }

    public void UpdateProfileIcon(ImageSource? source)
    {
        // Navigate correctly through ShellItem -> ShellSection -> ShellContent
        var profileItem = Items.FirstOrDefault()?.Items.FirstOrDefault()?.Items.FirstOrDefault() as ShellContent;
        if (profileItem is not null && profileItem.Route == "profile")
        {
            profileItem.Icon = source ?? "profile.svg";
        }
    }
}
