using System.Windows.Input;
using GetThere.Services;
using CommunityToolkit.Maui.Behaviors;

namespace GetThere.Components;

public partial class CustomBottomBar : ContentView
{
    public ICommand ProfileLongPressCommand { get; }

    public CustomBottomBar()
    {
        InitializeComponent();
        ProfileLongPressCommand = new Command(async () => await OnProfileLongPressed());
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler != null)
        {
            // Use dispatcher to delay the update until the UI is fully ready
            Dispatcher.Dispatch(() => UpdateActiveTab());
        }
    }

    private void UpdateActiveTab()
    {
        try
        {
            // Find behaviors by name if they are not automatically assigned as fields
            var profileTint = this.FindByName<IconTintColorBehavior>("ProfileTint");
            var mapTint = this.FindByName<IconTintColorBehavior>("MapTint");
            var operatorsTint = this.FindByName<IconTintColorBehavior>("OperatorsTint");

            // Safety check: if behaviors are not found or Shell is not ready, exit quietly
            if (profileTint == null || mapTint == null || operatorsTint == null) return;
            if (Shell.Current?.CurrentState?.Location == null) return;

            var route = Shell.Current.CurrentState.Location.ToString() ?? "";
            
            profileTint.TintColor = Colors.Gray;
            mapTint.TintColor = Colors.Gray;
            operatorsTint.TintColor = Colors.Gray;

            if (route.Contains("profile")) profileTint.TintColor = Color.FromArgb("#FF00FF");
            else if (route.Contains("map")) mapTint.TintColor = Color.FromArgb("#2196F3");
            else if (route.Contains("operators")) operatorsTint.TintColor = Color.FromArgb("#4CAF50");
        }
        catch
        {
            // Silently fail to avoid crashing the whole app if Shell route is being changed
        }
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("///profile");
    }

    private async Task OnProfileLongPressed()
    {
        bool confirm = await Shell.Current.DisplayAlertAsync("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (confirm)
        {
            // Resolve AuthService to log out properly
            var authService = IPlatformApplication.Current?.Services.GetService<AuthService>();
            authService?.Logout();
            
            App.GoToLogin();
        }
    }

    private async void OnMapTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("///map");
    }

    private async void OnOperatorsTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("///operators");
    }
}
