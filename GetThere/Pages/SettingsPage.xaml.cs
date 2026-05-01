#nullable enable
using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Dtos;
using System.Globalization;

namespace GetThere.Pages;

/// <summary>
/// Settings page — lets the user pick their country and app language.
/// The selection is persisted via <see cref="CountryPreferenceService"/>
/// and <see cref="LocalizationService"/> and used throughout the app.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;
    private readonly AuthService _authService;
    private List<CountryDto> _countries = [];

    private static readonly (string Code, string Key)[] _languages =
    [
        ("en", "Settings_LanguageEnglish"),
        ("hr", "Settings_LanguageCroatian"),
    ];

    public SettingsPage(CountryService countryService, CountryPreferenceService prefs, AuthService authService)
    {
        InitializeComponent();
        _countryService = countryService;
        _prefs = prefs;
        _authService = authService;
        SizeChanged += OnPageSizeChanged;
        PopulateLanguagePicker();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateResponsiveLayout();
        await LoadCountriesAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        PageUtility.ApplyTicketsStyleResponsive(Width, SettingsContent);
    }

    private void PopulateLanguagePicker()
    {
        var loc = LocalizationService.Instance;
        LanguagePicker.ItemsSource = _languages.Select(l => loc[l.Key]).ToList();

        var currentLang = Preferences.Default.Get("app_language",
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        var idx = Array.FindIndex(_languages, l => l.Code == currentLang);
        if (idx < 0) idx = 0;
        LanguagePicker.SelectedIndex = idx;
    }

    private async Task LoadCountriesAsync()
    {
        CountryLoader.IsVisible = CountryLoader.IsRunning = true;
        try
        {
            var result = await _countryService.GetCountriesAsync();
            _countries = result.Success && result.Data is not null ? result.Data : [];
            CountryPicker.ItemsSource = _countries.Select(c => c.Name).ToList();

            var currentId = _prefs.GetSelectedCountryId();
            if (currentId != -1)
            {
                var idx = _countries.FindIndex(c => c.Id == currentId);
                if (idx >= 0)
                    CountryPicker.SelectedIndex = idx;

                CurrentCountryLabel.Text = string.Format(
                    LocalizationService.Instance["Settings_CurrentCountry"],
                    _prefs.GetSelectedCountryName());
                CurrentCountryLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                LocalizationService.Instance["App_Error"],
                LocalizationService.Instance["Error_CouldNotLoadCountries"] + ex.Message,
                LocalizationService.Instance["App_Ok"]);
        }
        finally
        {
            CountryLoader.IsVisible = CountryLoader.IsRunning = false;
        }
    }

    private void OnCountrySelected(object? sender, EventArgs e)
    {
        var idx = CountryPicker.SelectedIndex;
        if (idx < 0 || idx >= _countries.Count) return;

        var country = _countries[idx];
        _prefs.SetSelectedCountry(country.Id, country.Name);
        CurrentCountryLabel.Text = string.Format(
            LocalizationService.Instance["Settings_CountrySaved"],
            country.Name);
        CurrentCountryLabel.IsVisible = true;
    }

    private void OnLanguageSelected(object? sender, EventArgs e)
    {
        var idx = LanguagePicker.SelectedIndex;
        if (idx < 0 || idx >= _languages.Length) return;

        var (code, _) = _languages[idx];
        var culture = code == "hr" ? new CultureInfo("hr-HR") : new CultureInfo("en-US");
        LocalizationService.Instance.SetCulture(culture);

        LanguageSavedLabel.Text = LocalizationService.Instance["Settings_LanguageSaved"];
        LanguageSavedLabel.IsVisible = true;
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var loc = LocalizationService.Instance;
        var confirmed = await DisplayAlertAsync(
            loc["Settings_Logout"],
            loc["Settings_LogoutConfirm"],
            loc["Settings_LogoutButton"],
            loc["App_Cancel"]);
        if (!confirmed) return;

        await _authService.Logout();
        App.GoToLogin();
    }
}
