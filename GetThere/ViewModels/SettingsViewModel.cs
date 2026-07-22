using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly CountryService _countryService;
    private readonly CountryPreferenceService _prefs;
    private readonly AuthService _authService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentCountryText = string.Empty;

    [ObservableProperty]
    private bool _hasCurrentCountry;

    public ObservableCollection<CountryItem> Countries { get; } = [];

    public SettingsViewModel(CountryService countryService, CountryPreferenceService prefs, AuthService authService)
    {
        _countryService = countryService;
        _prefs = prefs;
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoadCountries()
    {
        IsLoading = true;
        try
        {
            var result = await _countryService.GetCountriesAsync();
            Countries.Clear();
            if (result.Success && result.Data is not null)
            {
                var currentId = _prefs.GetSelectedCountryId();
                foreach (var country in result.Data)
                {
                    Countries.Add(new CountryItem
                    {
                        Id = country.Id,
                        Name = country.Name,
                        IsSelected = country.Id == currentId
                    });
                }

                var selectedName = _prefs.GetSelectedCountryName();
                if (!string.IsNullOrEmpty(selectedName))
                {
                    CurrentCountryText = string.Format(
                        LocalizationService.Instance["Settings_CurrentCountry"], selectedName);
                    HasCurrentCountry = true;
                }
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                LocalizationService.Instance["App_Error"],
                LocalizationService.Instance["Error_CouldNotLoadCountries"] + ex.Message,
                LocalizationService.Instance["App_Ok"]);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectCountry(CountryItem country)
    {
        if (country is null) return;
        _prefs.SetSelectedCountry(country.Id, country.Name);
        CurrentCountryText = string.Format(
            LocalizationService.Instance["Settings_CountrySaved"], country.Name);
        HasCurrentCountry = true;

        foreach (var c in Countries)
            c.IsSelected = c.Id == country.Id;
    }

    [RelayCommand]
    private async Task Logout()
    {
        var confirmed = await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Settings_Logout"],
            LocalizationService.Instance["Settings_LogoutConfirm"],
            LocalizationService.Instance["Settings_LogoutButton"],
            LocalizationService.Instance["App_Cancel"]);

        if (!confirmed) return;

        await _authService.Logout();
        App.GoToLogin();
    }
}

public partial class CountryItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
