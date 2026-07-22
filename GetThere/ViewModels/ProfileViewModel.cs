using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

public enum SubSettingsMode
{
    None,
    LanguageRegion,
    ChangePassword
}

public partial class ProfileViewModel : BaseViewModel
{
    private readonly WalletService _walletService;
    private readonly AuthService _authService;
    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;

    private static readonly CultureInfo BalanceCulture = CultureInfo.GetCultureInfo("hr-HR");
    private string _currentBalanceText = "€0,00";

    [ObservableProperty]
    private string _displayName = LocalizationService.Instance["Profile_DefaultName"];

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _balanceText = "€0,00";

    [ObservableProperty]
    private bool _isBalanceHidden;

    [ObservableProperty]
    private string _balanceVisibilityIcon = "eye.png";

    [ObservableProperty]
    private double _balanceVisibilityOpacity = 1.0;

    [ObservableProperty]
    private bool _isWalletTab = true;

    [ObservableProperty]
    private bool _isAccountTab;

    [ObservableProperty]
    private bool _isGuest;

    [ObservableProperty]
    private bool _hasTransactions;

    [ObservableProperty]
    private string _walletTabTextColor = "#512BD4";

    [ObservableProperty]
    private string _accountTabTextColor = "#64748B";

    [ObservableProperty]
    private double _tabIndicatorTranslation;

    public ObservableCollection<WalletTransactionResponse> Transactions { get; } = [];

    public ProfileViewModel(WalletService walletService, AuthService authService, CountryService countryService, CountryPreferenceService prefs)
    {
        _walletService = walletService;
        _authService = authService;
        _countryService = countryService;
        _prefs = prefs;
    }

    [RelayCommand]
    private async Task LoadProfile()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var fullName = await _authService.GetFullNameAsync();
            var email = await _authService.GetEmailAsync();

            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email))
            {
                IsGuest = true;
                return;
            }

            IsGuest = false;
            DisplayName = string.IsNullOrWhiteSpace(fullName)
                ? LocalizationService.Instance["Profile_DefaultName"]
                : fullName;
            FullName = fullName ?? string.Empty;
            Email = email ?? string.Empty;

            await LoadWallet();
            await LoadHistory();
        }
        catch
        {
            await Shell.Current.DisplayAlert(
                LocalizationService.Instance["App_Error"],
                LocalizationService.Instance["Error_CouldNotLoadProfile"],
                LocalizationService.Instance["App_Ok"]);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadWallet()
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
        BalanceText = IsBalanceHidden ? GetMaskedBalanceText() : _currentBalanceText;
        BalanceVisibilityIcon = IsBalanceHidden ? "eye_cl.png" : "eye.png";
        BalanceVisibilityOpacity = IsBalanceHidden ? 0.88 : 1.0;
    }

    private string GetMaskedBalanceText()
    {
        if (string.IsNullOrWhiteSpace(_currentBalanceText))
            return LocalizationService.Instance["Profile_MaskedBalance"];

        return new string(_currentBalanceText
            .Select(ch => char.IsDigit(ch) ? '•' : ch)
            .ToArray());
    }

    private async Task LoadHistory()
    {
        try
        {
            var result = await _walletService.GetWalletAsync();
            Transactions.Clear();
            if (result.Success && result.Data is not null)
            {
                var list = result.Data.RecentTransactions
                    .OrderByDescending(t => t.CreatedAt).ToList();
                foreach (var t in list)
                    Transactions.Add(t);
            }
            HasTransactions = Transactions.Count > 0;
        }
        catch
        {
            HasTransactions = false;
        }
    }

    [RelayCommand]
    private void ToggleBalanceVisibility()
    {
        IsBalanceHidden = !IsBalanceHidden;
        UpdateBalanceDisplay();
    }

    [RelayCommand]
    private void SelectWalletTab()
    {
        IsWalletTab = true;
        IsAccountTab = false;
        WalletTabTextColor = "#512BD4";
        AccountTabTextColor = "#64748B";
        TabIndicatorTranslation = 0;
    }

    [RelayCommand]
    private void SelectAccountTab()
    {
        IsWalletTab = false;
        IsAccountTab = true;
        WalletTabTextColor = "#64748B";
        AccountTabTextColor = "#512BD4";
        TabIndicatorTranslation = 1;
    }

    [RelayCommand]
    private async Task TopUp()
    {
        var input = await Shell.Current.DisplayPromptAsync(
            LocalizationService.Instance["Profile_TopUp_Title"],
            LocalizationService.Instance["Profile_TopUp_AmountLabel"],
            LocalizationService.Instance["Profile_TopUp_Next"],
            LocalizationService.Instance["App_Cancel"],
            "10.00", -1, Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(input)) return;

        if (decimal.TryParse(input.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            try
            {
                var result = await _walletService.TopUpAsync(amount);
                if (result.Success)
                {
                    await LoadWallet();
                    await Shell.Current.DisplayAlert(
                        LocalizationService.Instance["Profile_SubSettings_Success"],
                        string.Format(LocalizationService.Instance["Profile_TopUp_Added"], amount),
                        LocalizationService.Instance["App_Ok"]);
                    await LoadHistory();
                }
                else
                {
                    await Shell.Current.DisplayAlert(
                        LocalizationService.Instance["Profile_TopUp_FailedTitle"],
                        result.Message ?? LocalizationService.Instance["Profile_TopUp_Failed"],
                        LocalizationService.Instance["App_Ok"]);
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert(
                    LocalizationService.Instance["App_Error"], ex.Message,
                    LocalizationService.Instance["App_Ok"]);
            }
        }
        else
        {
            await Shell.Current.DisplayAlert(
                LocalizationService.Instance["Profile_TopUp_InvalidAmountTitle"],
                LocalizationService.Instance["Profile_TopUp_InvalidAmount"],
                LocalizationService.Instance["App_Ok"]);
        }
    }

    [RelayCommand]
    private async Task Logout()
    {
        var confirmed = await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_SignOut"],
            LocalizationService.Instance["Profile_SignOutConfirm"],
            LocalizationService.Instance["App_Yes"],
            LocalizationService.Instance["App_No"]);

        if (!confirmed) return;

        AuthService.ClearGuest();
        await _authService.Logout();
        App.GoToLogin();
    }

    [RelayCommand]
    private async Task GoToLoginRegister()
    {
        App.GoToLogin();
    }

    [RelayCommand]
    private async Task OnPrivacy()
    {
        await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_PrivacyTitle"],
            LocalizationService.Instance["Profile_PrivacyMessage"],
            LocalizationService.Instance["App_Ok"]);
    }

    [RelayCommand]
    private async Task OnPaymentMethods()
    {
        await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_PaymentMethodsTitle"],
            LocalizationService.Instance["Profile_PaymentMethodsMessage"],
            LocalizationService.Instance["App_Ok"]);
    }

    [RelayCommand]
    private async Task OnHelpCenter()
    {
        await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_SupportTitle"],
            LocalizationService.Instance["Profile_SupportMessage"],
            LocalizationService.Instance["App_Ok"]);
    }

    [RelayCommand]
    private async Task OnAbout()
    {
        await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_AboutTitle"],
            LocalizationService.Instance["Profile_AboutMessage"],
            LocalizationService.Instance["App_Ok"]);
    }

    // ── Sub-settings language support ──

    [ObservableProperty]
    private bool _isSubSettingsVisible;

    [ObservableProperty]
    private string _subSettingsTitle = string.Empty;

    [ObservableProperty]
    private bool _isSubSettingsLoading;

    [ObservableProperty]
    private string _subSettingsCurrentPassword = string.Empty;

    [ObservableProperty]
    private string _subSettingsNewPassword = string.Empty;

    [ObservableProperty]
    private string _subSettingsConfirmPassword = string.Empty;

    public ObservableCollection<LanguageItem> SubSettingsLanguages { get; } = [];
    public ObservableCollection<CountryItem> SubSettingsCountries { get; } = [];

    [RelayCommand]
    private async Task ShowLanguageRegion()
    {
        SubSettingsTitle = LocalizationService.Instance["Profile_RegionTitle"];
        IsSubSettingsLoading = true;
        IsSubSettingsVisible = true;

        var currentLang = LocalizationService.Instance.CurrentCulture.TwoLetterISOLanguageName;
        SubSettingsLanguages.Clear();
        SubSettingsLanguages.Add(new LanguageItem { Code = "en", DisplayName = "English", IsSelected = currentLang == "en" });
        SubSettingsLanguages.Add(new LanguageItem { Code = "hr", DisplayName = "Hrvatski", IsSelected = currentLang == "hr" });

        try
        {
            var countriesResult = await _countryService.GetCountriesAsync();
            SubSettingsCountries.Clear();
            if (countriesResult.Success && countriesResult.Data is not null)
            {
                var currentId = _prefs.GetSelectedCountryId();
                foreach (var c in countriesResult.Data)
                    SubSettingsCountries.Add(new CountryItem { Id = c.Id, Name = c.Name, IsSelected = c.Id == currentId });
            }
        }
        finally
        {
            IsSubSettingsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectLanguage(LanguageItem language)
    {
        if (language is null) return;
        var culture = language.Code == "hr" ? new CultureInfo("hr-HR") : new CultureInfo("en-US");
        LocalizationService.Instance.SetCulture(culture);
        IsSubSettingsVisible = false;
        App.GoToApp();
    }

    [RelayCommand]
    private async Task SelectCountry(CountryItem country)
    {
        if (country is null) return;
        _prefs.SetSelectedCountry(country.Id, country.Name);
        IsSubSettingsVisible = false;
        await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_SubSettings_Success"],
            string.Format(LocalizationService.Instance["Profile_SubSettings_RegionUpdated"], country.Name),
            LocalizationService.Instance["App_Ok"]);
    }

    [RelayCommand]
    private void BackFromSubSettings()
    {
        IsSubSettingsVisible = false;
    }

    [RelayCommand]
    private void ShowChangePassword()
    {
        SubSettingsTitle = LocalizationService.Instance["Profile_ChangePassword"];
        SubSettingsCurrentPassword = string.Empty;
        SubSettingsNewPassword = string.Empty;
        SubSettingsConfirmPassword = string.Empty;
        IsSubSettingsVisible = true;
    }

    [RelayCommand]
    private async Task SubmitChangePassword()
    {
        if (string.IsNullOrWhiteSpace(SubSettingsNewPassword) || SubSettingsNewPassword != SubSettingsConfirmPassword)
        {
            await Shell.Current.DisplayAlert(
                LocalizationService.Instance["App_Error"],
                LocalizationService.Instance["Profile_SubSettings_PasswordMismatch"],
                LocalizationService.Instance["App_Ok"]);
            return;
        }
        await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Profile_SubSettings_ComingSoon"],
            LocalizationService.Instance["Profile_SubSettings_PasswordComingSoon"],
            LocalizationService.Instance["App_Ok"]);
        IsSubSettingsVisible = false;
    }
}

public partial class LanguageItem : ObservableObject
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
