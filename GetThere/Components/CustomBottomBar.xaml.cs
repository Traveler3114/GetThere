using System;
using System.Windows.Input;
using System.Threading.Tasks;
using GetThere.Services;
using CommunityToolkit.Maui.Behaviors;
using Microsoft.Maui.Controls;

namespace GetThere.Components;

public partial class CustomBottomBar : ContentView
{
    private System.Timers.Timer? _iconTimer;
    private int _iconIndex = 0;
    private readonly string[] _icons = { "tab_bus.png", "tab_tram.png", "tab_train.png" };

    public CustomBottomBar()
    {
        InitializeComponent();
        StartIconRotation();
    }

    private void StartIconRotation()
    {
        _iconTimer = new System.Timers.Timer(3000);
        _iconTimer.Elapsed += (s, e) => {
            _iconIndex = (_iconIndex + 1) % _icons.Length;
            MainThread.BeginInvokeOnMainThread(() => {
                if (OperatorsIcon != null)
                {
                    OperatorsIcon.Source = _icons[_iconIndex];
                }
            });
        };
        _iconTimer.AutoReset = true;
        _iconTimer.Start();
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

    private async void OnConfirmLogoutClicked(object? sender, EventArgs e)
    {
        bool confirm = await Shell.Current.DisplayAlert("Logout", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;

        // Perform actual logout
        var authService = IPlatformApplication.Current?.Services.GetService<AuthService>();
        authService?.Logout();
        
        App.GoToLogin();
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
