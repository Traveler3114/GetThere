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

    public static readonly BindableProperty IsIslandModeProperty =
        BindableProperty.Create(nameof(IsIslandMode), typeof(bool), typeof(CustomBottomBar), false, propertyChanged: OnIsIslandModeChanged);

    public bool IsIslandMode
    {
        get => (bool)GetValue(IsIslandModeProperty);
        set => SetValue(IsIslandModeProperty, value);
    }

    private static void OnIsIslandModeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CustomBottomBar bar)
        {
            bar.UpdateIslandState();
        }
    }

    public CustomBottomBar()
    {
        InitializeComponent();
        StartIconRotation();

        // Ensure we collapse when navigating back to this page
        if (Shell.Current != null)
        {
            Shell.Current.Navigated += (s, e) => 
            {
                Dispatcher.Dispatch(() => UpdateActiveTab());
            };
        }
    }

    private void UpdateIslandState()
    {
        // Safety check to ensure XAML elements are loaded
        if (ExpandedBar == null || CollapsedIsland == null) return;

        if (IsIslandMode)
        {
            ExpandedBar.IsVisible = false;
            ExpandedBar.Opacity = 0;
            ExpandedBar.TranslationY = 20;
            CollapsedIsland.IsVisible = true;
            CollapsedIsland.Opacity = 1;
        }
        else
        {
            ExpandedBar.IsVisible = true;
            ExpandedBar.Opacity = 1;
            ExpandedBar.TranslationY = 0;
            CollapsedIsland.IsVisible = false;
            CollapsedIsland.Opacity = 0;
        }
    }

    private async void OnExpandClicked(object? sender, EventArgs e)
    {
        if (ExpandedBar == null || CollapsedIsland == null) return;

        // Animate expansion
        CollapsedIsland.IsVisible = false;
        ExpandedBar.IsVisible = true;
        
        await Task.WhenAll(
            ExpandedBar.FadeToAsync(1, 250),
            ExpandedBar.TranslateToAsync(0, 0, 250, Easing.CubicOut)
        );
    }

    private void StartIconRotation()
    {
        _iconTimer = new System.Timers.Timer(3000);
        _iconTimer.Elapsed += (s, e) => {
            _iconIndex = (_iconIndex + 1) % _icons.Length;
            MainThread.BeginInvokeOnMainThread(() => {
                var iconSource = _icons[_iconIndex];
                if (OperatorsIcon != null)
                {
                    OperatorsIcon.Source = iconSource;
                }
                // If we are in island mode on the operators page, update the island icon too
                var islandIcon = this.FindByName<Image>("IslandIcon");
                if (IsIslandMode && islandIcon != null && IsCurrentRoute("operators"))
                {
                    islandIcon.Source = iconSource;
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
            UpdateIslandState();

            // Find behaviors by name if they are not automatically assigned as fields
            var profileTint = this.FindByName<IconTintColorBehavior>("ProfileTint");
            var mapTint = this.FindByName<IconTintColorBehavior>("MapTint");
            var operatorsTint = this.FindByName<IconTintColorBehavior>("OperatorsTint");

            var islandIcon = this.FindByName<Image>("IslandIcon");
            var islandTint = this.FindByName<IconTintColorBehavior>("IslandTint");
            var islandShadow = this.FindByName<Shadow>("IslandShadow");

            // Safety check: if behaviors are not found or Shell is not ready, exit quietly
            if (profileTint == null || mapTint == null || operatorsTint == null) return;
            if (Shell.Current?.CurrentState?.Location == null) return;

            var route = Shell.Current.CurrentState.Location.ToString() ?? "";
            
            profileTint.TintColor = Colors.Gray;
            mapTint.TintColor = Colors.Gray;
            operatorsTint.TintColor = Colors.Gray;

            // Default island state
            if (islandTint != null) islandTint.TintColor = Colors.Gray;
            if (islandShadow != null) islandShadow.Brush = Colors.Transparent;

            if (route.Contains("profile"))
            {
                var color = Color.FromArgb("#FF00FF");
                profileTint.TintColor = color;
                if (islandIcon != null) islandIcon.Source = "icon_profile.svg";
                if (islandTint != null) islandTint.TintColor = color;
                if (islandShadow != null) islandShadow.Brush = color;
            }
            else if (route.Contains("map"))
            {
                var color = Color.FromArgb("#2196F3");
                mapTint.TintColor = color;
                if (islandIcon != null) islandIcon.Source = "icon_location_pin.svg";
                if (islandTint != null) islandTint.TintColor = color;
                if (islandShadow != null) islandShadow.Brush = color;
            }
            else if (route.Contains("operators"))
            {
                var color = Color.FromArgb("#4CAF50");
                operatorsTint.TintColor = color;
                if (islandIcon != null) islandIcon.Source = _icons[_iconIndex];
                if (islandTint != null) islandTint.TintColor = color;
                if (islandShadow != null) islandShadow.Brush = color;
            }
        }
        catch
        {
            // Silently fail to avoid crashing the whole app if Shell route is being changed
        }
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        if (IsIslandMode && !CollapsedIsland.IsVisible && IsCurrentRoute("profile"))
        {
            await CollapseAsync();
            return;
        }
        await Shell.Current.GoToAsync("///profile");
    }

    private async void OnConfirmLogoutClicked(object? sender, EventArgs e)
    {
        bool confirm = await Shell.Current.DisplayAlertAsync("Logout", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;

        // Perform actual logout
        var authService = IPlatformApplication.Current?.Services.GetService<AuthService>();
        authService?.Logout();
        
        App.GoToLogin();
    }

    private async void OnMapTapped(object? sender, TappedEventArgs e)
    {
        if (IsIslandMode && !CollapsedIsland.IsVisible && IsCurrentRoute("map"))
        {
            await CollapseAsync();
            return;
        }
        await Shell.Current.GoToAsync("///map");
    }

    private async void OnOperatorsTapped(object? sender, TappedEventArgs e)
    {
        if (IsIslandMode && !CollapsedIsland.IsVisible && IsCurrentRoute("operators"))
        {
            await CollapseAsync();
            return;
        }
        await Shell.Current.GoToAsync("///operators");
    }

    private bool IsCurrentRoute(string target)
    {
        var location = Shell.Current?.CurrentState?.Location?.ToString() ?? "";
        return location.Contains(target);
    }

    private async Task CollapseAsync()
    {
        await Task.WhenAll(
            ExpandedBar.FadeToAsync(0, 200),
            ExpandedBar.TranslateToAsync(0, 20, 200, Easing.CubicIn)
        );
        
        ExpandedBar.IsVisible = false;
        CollapsedIsland.IsVisible = true;
        CollapsedIsland.Opacity = 0;
        await CollapsedIsland.FadeToAsync(1, 150);
    }
}
