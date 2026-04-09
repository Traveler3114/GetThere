#nullable enable
using GetThere.Services;
using GetThere.State;
using GetThereShared.Dtos;

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
    private List<CountryDto> _countries = [];

    public SettingsPage(CountryService countryService, CountryPreferenceService prefs, AuthService authService)
    {
        InitializeComponent();
        _countryService = countryService;
        _prefs = prefs;
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCountriesAsync();
    }

    private async Task LoadCountriesAsync()
    {
        CountryLoader.IsVisible = CountryLoader.IsRunning = true;
        try
        {
            _countries = await _countryService.GetCountriesAsync() ?? [];
            CountryPicker.ItemsSource = _countries.Select(c => c.Name).ToList();

            // Pre-select current preference if any
            var currentId = _prefs.GetSelectedCountryId();
            if (currentId != -1)
            {
                var idx = _countries.FindIndex(c => c.Id == currentId);
                if (idx >= 0)
                    CountryPicker.SelectedIndex = idx;

                CurrentCountryLabel.Text = $"Current: {_prefs.GetSelectedCountryName()}";
                CurrentCountryLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not load countries: " + ex.Message, "OK");
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
        CurrentCountryLabel.Text = $"Saved: {country.Name} ✓";
        CurrentCountryLabel.IsVisible = true;
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync("Log out", "Do you want to log out?", "Log out", "Cancel");
        if (!confirmed) return;

        _authService.Logout();
        App.GoToLogin();
    }
}
