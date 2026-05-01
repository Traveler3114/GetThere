#nullable enable
using GetThere.Helpers;
using GetThere.Localization;
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
    private bool _isAccountTabSelected;

    public ProfilePage(WalletService walletService, PaymentService paymentService, AuthService authService, CountryService countryService, CountryPreferenceService prefs)
    {
        InitializeComponent();
        _walletService = walletService;
        _paymentService = paymentService;
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
        var loc = LocalizationService.Instance;
        var input = await DisplayPromptAsync(
            loc["Profile_TopUp_Title"],
            loc["Profile_TopUp_AmountLabel"],
            loc["Profile_TopUp_Next"],
            loc["App_Cancel"],
            "10.00", -1, Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(input)) return;

        if (decimal.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            try
            {
                var provResult = await _paymentService.GetProvidersAsync();
                if (provResult.Success && provResult.Data != null && provResult.Data.Any())
                {
                    var providers = provResult.Data.ToList();
                    var chosen = await DisplayActionSheetAsync(
                        loc["Profile_TopUp_Provider"],
                        loc["App_Cancel"],
                        null,
                        providers.Select(p => p.Name).ToArray());
                    if (chosen != null && chosen != loc["App_Cancel"])
                    {
                        var provider = providers.First(p => p.Name == chosen);
                        var success = await _paymentService.TopUpAsync(new TopUpDto { Amount = amount, PaymentProviderId = provider.Id });
                        if (success.Success)
                        {
                            await LoadWalletAsync();
                            await DisplayAlertAsync(
                                loc["Profile_SubSettings_Success"],
                                string.Format(loc["Profile_TopUp_Added"], amount),
                                loc["App_Ok"]);
                            await LoadHistoryAsync();
                        }
                        else
                        {
                            await DisplayAlertAsync(
                                loc["Profile_TopUp_FailedTitle"],
                                string.IsNullOrWhiteSpace(success.Message) ? loc["Profile_TopUp_Failed"] : success.Message,
                                loc["App_Ok"]);
                        }
                    }
                }
                else
                {
                    await DisplayAlertAsync(
                        loc["Profile_TopUp_ProviderErrorTitle"],
                        string.IsNullOrWhiteSpace(provResult.Message) ? loc["Profile_TopUp_NoProviders"] : provResult.Message,
                        loc["App_Ok"]);
                }
            }
            catch (Exception ex) { await DisplayAlertAsync(loc["App_Error"], ex.Message, loc["App_Ok"]); }
        }
        else
        {
            await DisplayAlertAsync(
                loc["Profile_TopUp_InvalidAmountTitle"],
                loc["Profile_TopUp_InvalidAmount"],
                loc["App_Ok"]);
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var loc = LocalizationService.Instance;
        if (await DisplayAlertAsync(loc["Profile_SignOut"], loc["Profile_SignOutConfirm"], loc["Profile_SignOutButton"], loc["App_Cancel"]))
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
        
        WalletTabBtn.TextColor = Color.FromArgb("#512BD4");
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
        var loc = LocalizationService.Instance;
        SubSettingsTitle.Text = loc["Profile_LanguageRegion"];
        SubSettingsView.IsVisible = true;
        SubSettingsContent.Clear();
        SubSettingsLoader.IsVisible = SubSettingsLoader.IsRunning = true;

        try
        {
            var countriesResult = await _countryService.GetCountriesAsync();
            if (!countriesResult.Success || countriesResult.Data is null) return;

            var countries = countriesResult.Data;
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var textColor = isDark ? Colors.White : Colors.Black;
            var pickerBg = isDark ? Color.FromArgb("#1F2937") : Colors.White;

            // ── Language ──
            SubSettingsContent.Add(new Label
            {
                Text = loc["Settings_Language"],
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                Margin = new Thickness(4, 0, 0, 8)
            });

            var languages = new[] { ("en", "Settings_LanguageEnglish"), ("hr", "Settings_LanguageCroatian") };
            var currentLang = Preferences.Default.Get("app_language", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            var langPicker = new Picker
            {
                Title = loc["Settings_Language"],
                FontSize = 16,
                TextColor = textColor,
                BackgroundColor = pickerBg,
                ItemsSource = languages.Select(l => loc[l.Item2]).ToList()
            };
            var langIdx = Array.FindIndex(languages, l => l.Item1 == currentLang);
            langPicker.SelectedIndex = langIdx >= 0 ? langIdx : 0;
            SubSettingsContent.Add(langPicker);

            SubSettingsContent.Add(new BoxView { HeightRequest = 24, Color = Colors.Transparent });

            // ── Region ──
            SubSettingsContent.Add(new Label
            {
                Text = loc["Profile_SubSettings_Region"],
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                Margin = new Thickness(4, 0, 0, 8)
            });

            var regionPicker = new Picker
            {
                Title = loc["Settings_Country"],
                FontSize = 16,
                TextColor = textColor,
                BackgroundColor = pickerBg,
                ItemsSource = countries.Select(c => c.Name).ToList()
            };
            var currentId = _prefs.GetSelectedCountryId();
            var countryIdx = countries.FindIndex(c => c.Id == currentId);
            regionPicker.SelectedIndex = countryIdx >= 0 ? countryIdx : 0;
            SubSettingsContent.Add(regionPicker);

            SubSettingsContent.Add(new BoxView { HeightRequest = 32, Color = Colors.Transparent });

            // ── Save button ──
            var saveBtn = new Button
            {
                Text = loc["Profile_SubSettings_SaveButton"],
                FontAttributes = FontAttributes.Bold,
                FontSize = 15,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#0EA5A4"),
                CornerRadius = 16,
                HeightRequest = 50,
                HorizontalOptions = LayoutOptions.Fill
            };

            saveBtn.Clicked += async (_, _) =>
            {
                var lIdx = langPicker.SelectedIndex;
                if (lIdx >= 0)
                {
                    var (code, _) = languages[lIdx];
                    var culture = code == "hr" ? new CultureInfo("hr-HR") : new CultureInfo("en-US");
                    LocalizationService.Instance.SetCulture(culture);
                }

                var rIdx = regionPicker.SelectedIndex;
                if (rIdx >= 0 && rIdx < countries.Count)
                {
                    var country = countries[rIdx];
                    _prefs.SetSelectedCountry(country.Id, country.Name);
                }

                SubSettingsView.IsVisible = false;
                await DisplayAlertAsync(
                    LocalizationService.Instance["Profile_SubSettings_Success"],
                    LocalizationService.Instance["Profile_SubSettings_SavedMessage"],
                    LocalizationService.Instance["App_Ok"]);
            };

            SubSettingsContent.Add(saveBtn);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                LocalizationService.Instance["App_Error"],
                LocalizationService.Instance["Error_CouldNotLoadRegions"] + ex.Message,
                LocalizationService.Instance["App_Ok"]);
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
        var loc = LocalizationService.Instance;
        SubSettingsTitle.Text = loc["Profile_ChangePassword"];
        SubSettingsView.IsVisible = true;
        SubSettingsContent.Clear();

        Func<string, Entry> createEntry = (placeholder) => new Entry
        {
            Placeholder = placeholder,
            IsPassword = true,
            FontSize = 16,
            Margin = new Thickness(0, 5, 0, 15),
            TextColor = Colors.Black,
            PlaceholderColor = Color.FromArgb("#94A3B8")
        };

        var currentPwd = createEntry(loc["Profile_SubSettings_CurrentPassword"]);
        var newPwd = createEntry(loc["Profile_SubSettings_NewPassword"]);
        var confirmPwd = createEntry(loc["Profile_SubSettings_ConfirmNewPassword"]);

        var updateBtn = new Button
        {
            Text = loc["Profile_SubSettings_UpdateButton"],
            BackgroundColor = Color.FromArgb("#512BD4"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            HeightRequest = 56,
            Margin = new Thickness(0, 10, 0, 0)
        };

        updateBtn.Clicked += async (s, e2) =>
        {
            var l = LocalizationService.Instance;
            if (string.IsNullOrWhiteSpace(newPwd.Text) || newPwd.Text != confirmPwd.Text)
            {
                await DisplayAlertAsync(l["App_Error"], l["Profile_SubSettings_PasswordMismatch"], l["App_Ok"]);
                return;
            }
            await DisplayAlertAsync(l["Profile_SubSettings_Success"], l["Profile_SubSettings_PasswordSuccess"], l["App_Ok"]);
            SubSettingsView.IsVisible = false;
        };

        SubSettingsContent.Add(new Label { Text = loc["Profile_SubSettings_PasswordDesc"], FontSize = 14, TextColor = Color.FromArgb("#64748B"), Margin = new Thickness(0, 0, 0, 20) });
        SubSettingsContent.Add(new Label { Text = loc["Profile_SubSettings_CurrentPassword"], FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
        SubSettingsContent.Add(currentPwd);
        SubSettingsContent.Add(new Label { Text = loc["Profile_SubSettings_NewPassword"], FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
        SubSettingsContent.Add(newPwd);
        SubSettingsContent.Add(new Label { Text = loc["Profile_SubSettings_ConfirmNewPassword"], FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
        SubSettingsContent.Add(confirmPwd);
        SubSettingsContent.Add(updateBtn);
    }

    private async void OnAvatarClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheetAsync("Profile Photo", "Cancel", null, "Take Photo", "Upload Image");
        
        if (action == "Take Photo" || action == "Upload Image")
        {
            await DisplayAlertAsync("Photo", "Camera/Gallery integration would go here.", "OK");
        }
    }

    private async void OnPrivacyClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync("Privacy", "Your data is encrypted and secure.", "OK");

    private async void OnPaymentMethodsClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync("Payments", "Manage your credit cards and digital wallets in the next update.", "OK");

    private async void OnHelpCenterClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync("Support", "Visit our help center at support.getthere.com", "OK");

    private async void OnAboutClicked(object? sender, EventArgs e) => 
        await DisplayAlertAsync("About", "GetThere v1.0\nProfessional Mobility Platform", "OK");
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
