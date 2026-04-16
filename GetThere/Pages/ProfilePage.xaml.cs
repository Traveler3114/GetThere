#nullable enable
using GetThere.Helpers;
using System;
using GetThere.Services;
using GetThere.State;
using GetThere.Components;
using Microsoft.Maui.Controls.Shapes;
using GetThereShared.Dtos;
using System.Globalization;

namespace GetThere.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly WalletService _walletService;
    private readonly PaymentService _paymentService;
    private readonly AuthService _authService;
    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;

    private bool _isBalanceHidden = false;
    private static readonly CultureInfo BalanceCulture = CultureInfo.GetCultureInfo("hr-HR");
    private string _currentBalanceText = "€0,00";
    private bool _isLoading;
    private CancellationTokenSource? _loadingCts;

    public ProfilePage(WalletService walletService, PaymentService paymentService, AuthService authService, CountryService countryService, CountryPreferenceService prefs)
    {
        InitializeComponent();
        _walletService = walletService;
        _paymentService = paymentService;
        _authService = authService;
        _countryService = countryService;
        _prefs = prefs;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateResponsiveLayout();
        await LoadProfileAsync();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        
        // Reset sub-settings overlay when re-entering or tapping the same tab
        if (SubSettingsView != null && SubSettingsView.IsVisible)
        {
            SubSettingsView.IsVisible = false;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        // Layout adjusted via XAML mainly
    }

    private async Task LoadProfileAsync()
    {
        if (_isLoading) return;

        _isLoading = true;
        StartLoadingAnimations();

        try
        {
            var fullName = await _authService.GetFullNameAsync();
            var email = await _authService.GetEmailAsync();

            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email))
            {
                GuestOverlay.IsVisible = true;
                return;
            }

            GuestOverlay.IsVisible = false;
            
            // Populate Identity & Account info
            UserDisplayNameLabel.Text = string.IsNullOrWhiteSpace(fullName) ? "User" : fullName;
            AccountFullNameLabel.Text = fullName;
            AccountEmailLabel.Text = email;

            await LoadWalletAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not load profile: " + ex.Message, "OK");
        }
        finally
        {
            StopLoadingAnimations();
            _isLoading = false;
        }
    }

    private async Task LoadWalletAsync()
    {
        var result = await _walletService.GetWalletAsync();
        if (result.Success && result.Data != null)
        {
            _currentBalanceText = $"€{result.Data.Balance.ToString("N2", BalanceCulture)}";
            UpdateBalanceDisplay();
        }
    }

    private void UpdateBalanceDisplay()
    {
        BalanceLabelOnCard.Text = _isBalanceHidden ? GetMaskedBalanceText() : _currentBalanceText;
        BalanceVisibilityIcon.Source = _isBalanceHidden ? "eye_cl.png" : "eye.png";
        BalanceVisibilityButton.Opacity = _isBalanceHidden ? 0.88 : 1.0;
    }

    private string GetMaskedBalanceText()
    {
        if (string.IsNullOrWhiteSpace(_currentBalanceText))
            return "EUR •••,••";

        return new string(_currentBalanceText
            .Select(ch => char.IsDigit(ch) ? '•' : ch)
            .ToArray());
    }

    private void OnHideBalanceTapped(object sender, TappedEventArgs e)
    {
        OnHideBalanceClicked(sender, EventArgs.Empty);
    }

    private async Task LoadHistoryAsync()
    {
        NoItemsLabel.IsVisible = false;

        try
        {
            var result = await _walletService.GetTransactionsAsync();
            if (result.Success && result.Data != null)
            {
                var list = result.Data.OrderByDescending(t => t.Timestamp).ToList();
                BindableLayout.SetItemsSource(HistoryRows, list);
                NoItemsLabel.IsVisible = !list.Any();
            }
        }
        catch
        {
            NoItemsLabel.IsVisible = true;
        }
    }

    private void OnLoginRegisterClicked(object? sender, EventArgs e)
    {
        App.GoToLogin();
    }


    private async void OnTopUpClicked(object? sender, EventArgs e)
    {
        var input = await DisplayPromptAsync("Top Up", "Amount (€):", "Next", "Cancel", "10.00", -1, Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(input)) return;

        if (decimal.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            try
            {
                var provResult = await _paymentService.GetProvidersAsync();
                if (provResult.Success && provResult.Data != null && provResult.Data.Any())
                {
                    var providers = provResult.Data.ToList();
                    var chosen = await DisplayActionSheetAsync("Provider", "Cancel", null, providers.Select(p => p.Name).ToArray());
                    if (chosen != null && chosen != "Cancel")
                    {
                        var provider = providers.First(p => p.Name == chosen);
                        var success = await _paymentService.TopUpAsync(new TopUpDto { Amount = amount, PaymentProviderId = provider.Id });
                        if (success.Success)
                        {
                            await LoadWalletAsync();
                            await DisplayAlertAsync("Success", $"€{amount:F2} added!", "OK");
                            await LoadHistoryAsync();
                        }
                    }
                }
            }
            catch (Exception ex) { await DisplayAlertAsync("Error", ex.Message, "OK"); }
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Logout", "Are you sure?", "Yes", "No"))
        {
            _authService.Logout();
            App.GoToLogin();
        }
    }

    private void OnWalletTabClicked(object sender, EventArgs e)
    {
        AccountTabView.IsVisible = false;
        AccountTabView.Opacity = 0;
        
        WalletTabView.IsVisible = true;
        WalletTabView.Opacity = 1;

        // Animate indicator back to start
        TabIndicator.TranslateTo(0, 0, 250, Easing.CubicInOut);
        
        WalletTabBtn.TextColor = Color.FromArgb("#512BD4");
        AccountTabBtn.TextColor = Color.FromArgb("#64748B");
    }

    private void OnAccountTabClicked(object sender, EventArgs e)
    {
        WalletTabView.IsVisible = false;
        WalletTabView.Opacity = 0;
        
        AccountTabView.IsVisible = true;
        AccountTabView.Opacity = 1;

        // Calculate distance to move (half of the grid width)
        double targetX = TabGrid.Width / 2;
        TabIndicator.TranslateTo(targetX, 0, 250, Easing.CubicInOut);

        WalletTabBtn.TextColor = Color.FromArgb("#64748B");
        AccountTabBtn.TextColor = Color.FromArgb("#512BD4");
    }

    private void OnMainScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        var scrollView = sender as ScrollView;
        if (scrollView == null) return;

        double scrollY = e.ScrollY;
        const double maxScroll = 120.0;
        
        // --- 1. Header Parallax & Scale ---
        double factor = Math.Clamp(scrollY / maxScroll, 0, 1);
        
        // Avatar & Info (Fastest reaction)
        UserAvatarBorder.Scale = 1.0 - (factor * 0.3);
        UserInfoStack.Opacity = 1.0 - (factor * 0.8);
        UserInfoStack.TranslationX = -(factor * 15);
        
        // Tab Switcher (Subtle reaction)
        TabSwitcherContainer.Scale = 1.0 - (factor * 0.1);
        TabSwitcherContainer.Opacity = 1.0 - (factor * 0.4);
        TabSwitcherContainer.TranslationY = -(factor * 5);
        
        // Balance Card (Deep parallax)
        if (BalanceCardGrid != null && WalletTabView.IsVisible)
        {
            BalanceCardGrid.TranslationY = -(factor * 20);
            BalanceCardGrid.Opacity = 1.0 - (factor * 0.15);
            BalanceCardGrid.Scale = 1.0 - (factor * 0.05);
        }
        
        if (SubSettingsView.IsVisible)
        {
            SubSettingsHeader.Opacity = 1.0 - (Math.Clamp(scrollY / 50.0, 0, 0.4));
        }

        // --- 2. Premium Manual Scrollbar Logic ---
        double contentHeight = scrollView.ContentSize.Height;
        double viewHeight = scrollView.Height;
        
        if (contentHeight > viewHeight)
        {
            CustomScrollThumb.IsVisible = true;
            // Thumb is now INSIDE the border grid
            double usableHeight = viewHeight - CustomScrollThumb.HeightRequest - 40; 
            double scrollPercent = Math.Clamp(scrollY / (contentHeight - viewHeight), 0, 1);
            CustomScrollThumb.TranslationY = (scrollPercent * usableHeight) + 20;
            CustomScrollThumb.Opacity = 0.5 + (scrollPercent * 0.5);
        }
        else
        {
            CustomScrollThumb.IsVisible = false;
        }
    }

    private void OnHideBalanceClicked(object sender, EventArgs e)
    {
        _isBalanceHidden = !_isBalanceHidden;
        UpdateBalanceDisplay();
    }

    private async void OnLanguageRegionClicked(object? sender, EventArgs e)
    {
        SubSettingsTitle.Text = "Language & Region";
        SubSettingsView.IsVisible = true;
        SubSettingsContent.Clear();
        SubSettingsLoader.IsVisible = SubSettingsLoader.IsRunning = true;

        try
        {
            var countries = await _countryService.GetCountriesAsync();
            if (countries == null) return;

            var currentId = _prefs.GetSelectedCountryId();

            foreach (var country in countries)
            {
                var isSelected = country.Id == currentId;
                
                var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = 20 };
                var label = new Label { Text = country.Name, FontSize = 16, VerticalOptions = LayoutOptions.Center, TextColor = Colors.Black };
                if (isSelected) label.FontAttributes = FontAttributes.Bold;

                row.Add(label);
                if (isSelected)
                {
                    row.Add(new Label { Text = "✓", TextColor = Color.FromArgb("#10B981"), FontSize = 18, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End }, 1);
                }

                var border = new Border
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = 0,
                    BackgroundColor = isSelected ? Color.FromArgb("#F0FDFA") : Colors.White,
                    StrokeThickness = isSelected ? 2 : 0,
                    Stroke = Color.FromArgb("#10B981"),
                    StrokeShape = new RoundRectangle { CornerRadius = 16 },
                    Content = row
                };

                border.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(async () =>
                    {
                        _prefs.SetSelectedCountry(country.Id, country.Name);
                        SubSettingsView.IsVisible = false;
                        await DisplayAlert("Success", $"Region updated to {country.Name}", "OK");
                    })
                });

                SubSettingsContent.Add(border);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", "Could not load regions: " + ex.Message, "OK");
        }
        finally
        {
            SubSettingsLoader.IsVisible = SubSettingsLoader.IsRunning = false;
        }
    }

    private void OnBackFromSubSettings(object sender, EventArgs e)
    {
        SubSettingsView.IsVisible = false;
    }

    private void OnChangePasswordClicked(object? sender, EventArgs e)
    {
        SubSettingsTitle.Text = "Change Password";
        SubSettingsView.IsVisible = true;
        SubSettingsContent.Clear();

        // Helper to create styled entries
        Func<string, Entry> createEntry = (placeholder) => new Entry 
        { 
            Placeholder = placeholder, 
            IsPassword = true,
            FontSize = 16,
            Margin = new Thickness(0, 5, 0, 15),
            TextColor = Colors.Black,
            PlaceholderColor = Color.FromArgb("#94A3B8")
        };

        var currentPwd = createEntry("Current Password");
        var newPwd = createEntry("New Password");
        var confirmPwd = createEntry("Confirm New Password");

        var updateBtn = new Button
        {
            Text = "Update Password",
            BackgroundColor = Color.FromArgb("#512BD4"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            HeightRequest = 56,
            Margin = new Thickness(0, 10, 0, 0)
        };

        updateBtn.Clicked += async (s, e2) => 
        {
            if (string.IsNullOrWhiteSpace(newPwd.Text) || newPwd.Text != confirmPwd.Text)
            {
                await DisplayAlert("Error", "Passwords do not match.", "OK");
                return;
            }
            // Mock success
            await DisplayAlert("Success", "Password updated successfully.", "OK");
            SubSettingsView.IsVisible = false;
        };

        SubSettingsContent.Add(new Label { Text = "Set a strong password to protect your account.", FontSize = 14, TextColor = Color.FromArgb("#64748B"), Margin = new Thickness(0,0,0,20) });
        SubSettingsContent.Add(new Label { Text = "Current Password", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
        SubSettingsContent.Add(currentPwd);
        SubSettingsContent.Add(new Label { Text = "New Password", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
        SubSettingsContent.Add(newPwd);
        SubSettingsContent.Add(new Label { Text = "Confirm New Password", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
        SubSettingsContent.Add(confirmPwd);
        SubSettingsContent.Add(updateBtn);
    }

    private async void OnAvatarClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheet("Profile Photo", "Cancel", null, "Take Photo", "Upload Image");
        
        if (action == "Take Photo" || action == "Upload Image")
        {
            await DisplayAlert("Photo", "Camera/Gallery integration would go here.", "OK");
        }
    }

    private async void OnPrivacyClicked(object? sender, EventArgs e) => 
        await DisplayAlert("Privacy", "Your data is encrypted and secure.", "OK");

    private async void OnPaymentMethodsClicked(object? sender, EventArgs e) => 
        await DisplayAlert("Payments", "Manage your credit cards and digital wallets in the next update.", "OK");

    private async void OnHelpCenterClicked(object? sender, EventArgs e) => 
        await DisplayAlert("Support", "Visit our help center at support.getthere.com", "OK");

    private async void OnAboutClicked(object? sender, EventArgs e) => 
        await DisplayAlert("About", "GetThere v1.0\nProfessional Mobility Platform", "OK");
    private async void StartLoadingAnimations()
    {
        PremiumLoadingState.IsVisible = true;
        // Hide main content rows to show skeleton
        UserHeader.IsVisible = false;
        TabSwitcherContainer.IsVisible = false;
        WalletTabView.IsVisible = false;

        _loadingCts = new CancellationTokenSource();
        var token = _loadingCts.Token;

        // 1. Shimmer sweep loop
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() => ShimmerBox.TranslationX = -500);
                await ShimmerBox.TranslateTo(1000, 0, 1500, Easing.CubicInOut);
                await Task.Delay(300, token);
            }
        }, token);

    }




    private void StopLoadingAnimations()
    {
        _loadingCts?.Cancel();
        PremiumLoadingState.IsVisible = false;
        // Reveal main content
        UserHeader.IsVisible = true;
        TabSwitcherContainer.IsVisible = true;
        WalletTabView.IsVisible = true;
    }
}
