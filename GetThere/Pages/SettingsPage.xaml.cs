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
    private const int AllCountriesId = -1;
    private const int NoCountryFilterId = -2;

    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;
    private List<CountryDto> _countries = [];

    public SettingsPage(CountryService countryService, CountryPreferenceService prefs)
    {
        InitializeComponent();
        _countryService = countryService;
        _prefs = prefs;
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
            var fetched = await _countryService.GetCountriesAsync() ?? [];
            _countries =
            [
                new CountryDto { Id = AllCountriesId, Name = "All countries (worldwide)" },
                new CountryDto { Id = NoCountryFilterId, Name = "None (no country filter)" },
                .. fetched
            ];
            CountryPicker.ItemsSource = _countries.Select(c => c.Name).ToList();

            // Pre-select current preference if any
            var currentId = _prefs.GetSelectedCountryId();
            if (currentId == AllCountriesId)
            {
                CountryPicker.SelectedIndex = 0;
                CurrentCountryLabel.Text = "Current: Worldwide";
                CurrentCountryLabel.IsVisible = true;
            }
            else
            {
                var idx = _countries.FindIndex(c => c.Id == currentId);
                CountryPicker.SelectedIndex = idx >= 0 ? idx : 0;

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
        if (country.Id <= 0)
        {
            _prefs.Clear();
            CurrentCountryLabel.Text = "Saved: Worldwide ✓";
            CurrentCountryLabel.IsVisible = true;
            return;
        }

        _prefs.SetSelectedCountry(country.Id, country.Name);
        CurrentCountryLabel.Text = $"Saved: {country.Name} ✓";
        CurrentCountryLabel.IsVisible = true;
    }
}
