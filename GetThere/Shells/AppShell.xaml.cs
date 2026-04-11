#nullable enable
using GetThere.State;
using Microsoft.Maui.Controls;

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
        if (profileItem != null && profileItem.Route == "profile")
        {
            profileItem.Icon = source ?? "profile.svg";
        }
    }
}
