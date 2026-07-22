using Microsoft.Maui.Controls;

using GetThere.Services;
using GetThere.State;

namespace GetThere;

public partial class AppShell : Shell
{
    public AppShell(IAnalyticsService analytics)
    {
        InitializeComponent();
        Navigated += (s, e) =>
        {
            if (e.Current?.Location is not null)
                analytics.TrackScreen(e.Current.Location.OriginalString);
        };
    }

    public void UpdateProfileIcon(ImageSource? source)
    {
        var profileItem = Items.FirstOrDefault()?.Items.FirstOrDefault()?.Items.FirstOrDefault() as ShellContent;
        if (profileItem is not null && profileItem.Route == "profile")
        {
            profileItem.Icon = source ?? "profile.svg";
        }
    }
}
