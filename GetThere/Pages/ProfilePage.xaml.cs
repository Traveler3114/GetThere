#nullable enable

using System;
using System.Globalization;

using Microsoft.Maui.Controls.Shapes;

using GetThere.Components;
using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Contracts;

namespace GetThere.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly WalletService _walletService;
    private readonly AuthService _authService;
    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;

    private bool _isBalanceHidden = false;
    private static readonly CultureInfo BalanceCulture = CultureInfo.GetCultureInfo("hr-HR");
    private string _currentBalanceText = "€0,00";
    private bool _isLoading;
    private bool _isAccountTabSelected;

    public ProfilePage(WalletService walletService, AuthService authService, CountryService countryService, CountryPreferenceService prefs)
    {
        InitializeComponent();
        _walletService = walletService;
        _authService = authService;
        _countryService = countryService;
        _prefs = prefs;
        ApplyThemeIcons();
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        => ApplyThemeIcons();

    private void ApplyThemeIcons()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var profileIcon = isDark ? "profile_white.svg" : "profile.svg";
        var ticketIcon = isDark ? "ticket_white.svg" : "ticket.svg";
        var mapIcon = isDark ? "map_white.svg" : "map.svg";
        var settingsIcon = isDark ? "settings_white.svg" : "settings.svg";
        var arrowIcon = isDark ? "arrow_right_white.svg" : "arrow_right.svg";

        PrivacyIcon.Source = profileIcon;
        PaymentMethodsIcon.Source = ticketIcon;
        LanguageRegionIcon.Source = mapIcon;
        ChangePasswordIcon.Source = settingsIcon;
        HelpCenterIcon.Source = settingsIcon;
        AboutIcon.Source = profileIcon;

        PrivacyArrowIcon.Source = arrowIcon;
        PaymentMethodsArrowIcon.Source = arrowIcon;
        LanguageRegionArrowIcon.Source = arrowIcon;
        ChangePasswordArrowIcon.Source = arrowIcon;
        HelpCenterArrowIcon.Source = arrowIcon;
        AboutArrowIcon.Source = arrowIcon;
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
        if (SubSettingsView is not null && SubSettingsView.IsVisible)
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
            UserDisplayNameLabel.Text = string.IsNullOrWhiteSpace(fullName) ? LocalizationService.Instance["Profile_DefaultName"] : fullName;
            AccountFullNameLabel.Text = fullName;
            AccountEmailLabel.Text = email;

            await LoadWalletAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(LocalizationService.Instance["App_Error"], LocalizationService.Instance["Error_CouldNotLoadProfile"] + ex.Message, LocalizationService.Instance["App_Ok"]);
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
        if (result.Success && result.Data is not null)
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
            return LocalizationService.Instance["Profile_MaskedBalance"];

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
            var result = await _walletService.GetWalletAsync();
            if (result.Success && result.Data is not null)
            {
                var list = result.Data.RecentTransactions
                    .OrderByDescending(t => t.CreatedAt).ToList();
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
        var input = await DisplayPromptAsync(LocalizationService.Instance["Profile_TopUp_Title"], LocalizationService.Instance["Profile_TopUp_AmountLabel"], LocalizationService.Instance["Profile_TopUp_Next"], LocalizationService.Instance["App_Cancel"], "10.00", -1, Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(input)) return;

        if (decimal.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            try
            {
                var result = await _walletService.TopUpAsync(amount);
                if (result.Success)
                {
                    await LoadWalletAsync();
                    await DisplayAlertAsync(LocalizationService.Instance["Profile_SubSettings_Success"], string.Format(LocalizationService.Instance["Profile_TopUp_Added"], amount), LocalizationService.Instance["App_Ok"]);
                    await LoadHistoryAsync();
                }
                else
                {
                    await DisplayAlertAsync(LocalizationService.Instance["Profile_TopUp_FailedTitle"], result.Message ?? LocalizationService.Instance["Profile_TopUp_Failed"], LocalizationService.Instance["App_Ok"]);
                }
            }
            catch (Exception ex) { await DisplayAlertAsync(LocalizationService.Instance["App_Error"], ex.Message, LocalizationService.Instance["App_Ok"]); }
        }
        else
        {
            await DisplayAlertAsync(LocalizationService.Instance["Profile_TopUp_InvalidAmountTitle"], LocalizationService.Instance["Profile_TopUp_InvalidAmount"], LocalizationService.Instance["App_Ok"]);
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync(LocalizationService.Instance["Profile_SignOut"], LocalizationService.Instance["Profile_SignOutConfirm"], LocalizationService.Instance["App_Yes"], LocalizationService.Instance["App_No"]))
        {
            await _authService.Logout();
            App.GoToLogin();
        }
    }

    private void OnWalletTabClicked(object sender, EventArgs e)
    {
        _isAccountTabSelected = false;
        AccountTabView.IsVisible = false;
        AccountTabView.Opacity = 0;
        
        WalletTabView.IsVisible = true;
        WalletTabView.Opacity = 1;

        // Animate indicator back to start
        TabIndicator.TranslateToAsync(0, 0, 250, Easing.CubicInOut);
        
        WalletTabBtn.TextColor = Color.FromArgb("#009688");
        AccountTabBtn.TextColor = Color.FromArgb("#64748B");
    }

    private void OnAccountTabClicked(object sender, EventArgs e)
    {
        _isAccountTabSelected = true;
        WalletTabView.IsVisible = false;
        WalletTabView.Opacity = 0;
        
        AccountTabView.IsVisible = true;
        AccountTabView.Opacity = 1;

        // Calculate distance to move (half of the grid width)
        double targetX = TabGrid.Width / 2;
        TabIndicator.TranslateToAsync(targetX, 0, 250, Easing.CubicInOut);

        WalletTabBtn.TextColor = Color.FromArgb("#64748B");
        AccountTabBtn.TextColor = Color.FromArgb("#009688");
    }

    private void OnMainScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        var scrollView = sender as ScrollView;
        if (scrollView is null) return;

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
        if (BalanceCardGrid is not null && WalletTabView.IsVisible)
        {
            BalanceCardGrid.TranslationY = -(factor * 20);
            BalanceCardGrid.Opacity = 1.0 - (factor * 0.15);
            BalanceCardGrid.Scale = 1.0 - (factor * 0.05);
        }
        
        if (SubSettingsView.IsVisible)
        {
            SubSettingsHeader.Opacity = 1.0 - (Math.Clamp(scrollY / 50.0, 0, 0.4));
        }

        // --- 2. Custom scrollbar logic ---
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
        SubSettingsTitle.Text = LocalizationService.Instance["Profile_RegionTitle"];
        SubSettingsView.IsVisible = true;
        SubSettingsContent.Clear();
        SubSettingsLoader.IsVisible = SubSettingsLoader.IsRunning = true;

        try
        {
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var bgColor = isDark ? Color.FromArgb("#111827") : Colors.White;
            var textColor = isDark ? Colors.White : Colors.Black;
            var accentBg = isDark ? Color.FromArgb("#134E4A") : Color.FromArgb("#F0FDFA");
            var accentStroke = Color.FromArgb("#10B981");

            // ── Language section header ──
            SubSettingsContent.Add(new Label
            {
                Text = LocalizationService.Instance["Settings_Language"],
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var currentLang = LocalizationService.Instance.CurrentCulture.TwoLetterISOLanguageName;

            // English option
            SubSettingsContent.Add(BuildLanguageOption("en", "English", currentLang == "en", bgColor, textColor, accentBg, accentStroke));
            // Croatian option
            SubSettingsContent.Add(BuildLanguageOption("hr", "Hrvatski", currentLang == "hr", bgColor, textColor, accentBg, accentStroke));

            // ── Separator ──
            SubSettingsContent.Add(new BoxView
            {
                HeightRequest = 1,
                Color = Color.FromArgb("#E5E7EB"),
                Margin = new Thickness(0, 16, 0, 16)
            });

            // ── Region section header ──
            SubSettingsContent.Add(new Label
            {
                Text = LocalizationService.Instance["Profile_SubSettings_Region"],
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var countriesResult = await _countryService.GetCountriesAsync();
            if (!countriesResult.Success || countriesResult.Data is null) return;

            var currentId = _prefs.GetSelectedCountryId();

            foreach (var country in countriesResult.Data)
            {
                var isSelected = country.Id == currentId;

                var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = 20 };
                var label = new Label { Text = country.Name, FontSize = 16, VerticalOptions = LayoutOptions.Center, TextColor = textColor };
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
                    BackgroundColor = isSelected ? accentBg : bgColor,
                    StrokeThickness = isSelected ? 2 : 0,
                    Stroke = accentStroke,
                    StrokeShape = new RoundRectangle { CornerRadius = 16 },
                    Content = row
                };

                border.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(async () =>
                    {
                        _prefs.SetSelectedCountry(country.Id, country.Name);
                        SubSettingsView.IsVisible = false;
                        await DisplayAlertAsync(LocalizationService.Instance["Profile_SubSettings_Success"], string.Format(LocalizationService.Instance["Profile_SubSettings_RegionUpdated"], country.Name), LocalizationService.Instance["App_Ok"]);
                    })
                });

                SubSettingsContent.Add(border);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(LocalizationService.Instance["App_Error"], LocalizationService.Instance["Error_CouldNotLoadRegions"] + ex.Message, LocalizationService.Instance["App_Ok"]);
        }
        finally
        {
            SubSettingsLoader.IsVisible = SubSettingsLoader.IsRunning = false;
        }
    }

    private Border BuildLanguageOption(string langCode, string displayName, bool isSelected, Color bgColor, Color textColor, Color accentBg, Color accentStroke)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = 20 };
        var label = new Label { Text = displayName, FontSize = 16, VerticalOptions = LayoutOptions.Center, TextColor = textColor };
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
            BackgroundColor = isSelected ? accentBg : bgColor,
            StrokeThickness = isSelected ? 2 : 0,
            Stroke = accentStroke,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = row
        };

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                var culture = langCode == "hr" ? new CultureInfo("hr-HR") : new CultureInfo("en-US");
                LocalizationService.Instance.SetCulture(culture);
                SubSettingsView.IsVisible = false;
                App.GoToApp();
            })
        });

        return border;
    }

    private void OnBackFromSubSettings(object sender, EventArgs e)
    {
        SubSettingsView.IsVisible = false;
    }

    private void OnChangePasswordClicked(object? sender, EventArgs e)
    {
        SubSettingsTitle.Text = LocalizationService.Instance["Profile_ChangePassword"];
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

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var textColor = isDark ? Colors.White : Colors.Black;
        var mutedColor = isDark ? Color.FromArgb("#94A3B8") : Color.FromArgb("#64748B");

        var currentPwd = createEntry(LocalizationService.Instance["Profile_SubSettings_CurrentPassword"]);
        var newPwd = createEntry(LocalizationService.Instance["Profile_SubSettings_NewPassword"]);
        var confirmPwd = createEntry(LocalizationService.Instance["Profile_SubSettings_ConfirmNewPassword"]);

        var updateBtn = new Button
        {
            Text = LocalizationService.Instance["Profile_SubSettings_UpdateButton"],
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
                await DisplayAlertAsync(LocalizationService.Instance["App_Error"], LocalizationService.Instance["Profile_SubSettings_PasswordMismatch"], LocalizationService.Instance["App_Ok"]);
                return;
            }
            await DisplayAlertAsync(LocalizationService.Instance["Profile_SubSettings_ComingSoon"], LocalizationService.Instance["Profile_SubSettings_PasswordComingSoon"], LocalizationService.Instance["App_Ok"]);
            SubSettingsView.IsVisible = false;
        };

        SubSettingsContent.Add(new Label { Text = LocalizationService.Instance["Profile_SubSettings_PasswordDesc"], FontSize = 14, TextColor = mutedColor, Margin = new Thickness(0,0,0,20) });
        SubSettingsContent.Add(new Label { Text = LocalizationService.Instance["Profile_SubSettings_CurrentPassword"], FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = textColor });
        SubSettingsContent.Add(currentPwd);
        SubSettingsContent.Add(new Label { Text = LocalizationService.Instance["Profile_SubSettings_NewPassword"], FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = textColor });
        SubSettingsContent.Add(newPwd);
        SubSettingsContent.Add(new Label { Text = LocalizationService.Instance["Profile_SubSettings_ConfirmNewPassword"], FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = textColor });
        SubSettingsContent.Add(confirmPwd);
        SubSettingsContent.Add(updateBtn);
    }

    private async void OnAvatarClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheetAsync(LocalizationService.Instance["Profile_PhotoTitle"], LocalizationService.Instance["App_Cancel"], null, LocalizationService.Instance["Profile_PhotoTake"], LocalizationService.Instance["Profile_PhotoUpload"]);
        
        if (action == LocalizationService.Instance["Profile_PhotoTake"] || action == LocalizationService.Instance["Profile_PhotoUpload"])
        {
            await DisplayAlertAsync(LocalizationService.Instance["Profile_PhotoTitle"], LocalizationService.Instance["Profile_PhotoResult"], LocalizationService.Instance["App_Ok"]);
        }
    }

    private async void OnPrivacyClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync(LocalizationService.Instance["Profile_PrivacyTitle"], LocalizationService.Instance["Profile_PrivacyMessage"], LocalizationService.Instance["App_Ok"]);

    private async void OnPaymentMethodsClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync(LocalizationService.Instance["Profile_PaymentMethodsTitle"], LocalizationService.Instance["Profile_PaymentMethodsMessage"], LocalizationService.Instance["App_Ok"]);

    private async void OnHelpCenterClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync(LocalizationService.Instance["Profile_SupportTitle"], LocalizationService.Instance["Profile_SupportMessage"], LocalizationService.Instance["App_Ok"]);

    private async void OnAboutClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync(LocalizationService.Instance["Profile_AboutTitle"], LocalizationService.Instance["Profile_AboutMessage"], LocalizationService.Instance["App_Ok"]);
    private void StartLoadingAnimations()
    {
        WalletLoadingState.IsVisible = !_isAccountTabSelected;
        AccountLoadingState.IsVisible = _isAccountTabSelected;
        UserHeader.IsVisible = false;
        TabSwitcherContainer.IsVisible = false;
        WalletTabView.IsVisible = false;
        AccountTabView.IsVisible = false;
    }




    private void StopLoadingAnimations()
    {
        WalletLoadingState.IsVisible = false;
        AccountLoadingState.IsVisible = false;
        UserHeader.IsVisible = true;
        TabSwitcherContainer.IsVisible = true;
        WalletTabView.IsVisible = !_isAccountTabSelected;
        AccountTabView.IsVisible = _isAccountTabSelected;
        WalletTabView.Opacity = _isAccountTabSelected ? 0 : 1;
        AccountTabView.Opacity = _isAccountTabSelected ? 1 : 0;
    }
}
