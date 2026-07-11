using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Contracts;

namespace GetThere.Pages;

/// <summary>
/// Settings page — lets the user pick their country.
/// The selection is persisted via <see cref="CountryPreferenceService"/>
/// and used to filter operators and services throughout the app.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;
    private readonly AuthService _authService;
    private List<CountryResponse> _countries = [];

    public SettingsPage(CountryService countryService, CountryPreferenceService prefs, AuthService authService)
    {
        InitializeComponent();
        _countryService = countryService;
        _prefs = prefs;
        _authService = authService;
        SizeChanged += OnPageSizeChanged;
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

    private async Task LoadCountriesAsync()
    {
        CountryLoader.IsVisible = CountryLoader.IsRunning = true;
        try
        {
            var result = await _countryService.GetCountriesAsync();
            _countries = result.Success && result.Data is not null ? result.Data : [];
            CountryPicker.ItemsSource = _countries.Select(c => c.Name).ToList();

            // Pre-select current preference if any
            var currentId = _prefs.GetSelectedCountryId();
            if (currentId != -1)
            {
                var idx = _countries.FindIndex(c => c.Id == currentId);
                if (idx >= 0)
                    CountryPicker.SelectedIndex = idx;

                CurrentCountryLabel.Text = string.Format(LocalizationService.Instance["Settings_CurrentCountry"], _prefs.GetSelectedCountryName());
                CurrentCountryLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(LocalizationService.Instance["App_Error"], LocalizationService.Instance["Error_CouldNotLoadCountries"] + ex.Message, LocalizationService.Instance["App_Ok"]);
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
        CurrentCountryLabel.Text = string.Format(LocalizationService.Instance["Settings_CountrySaved"], country.Name);
        CurrentCountryLabel.IsVisible = true;
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(LocalizationService.Instance["Settings_Logout"], LocalizationService.Instance["Settings_LogoutConfirm"], LocalizationService.Instance["Settings_LogoutButton"], LocalizationService.Instance["App_Cancel"]);
        if (!confirmed) return;

        await _authService.Logout();
        App.GoToLogin();
    }
}
