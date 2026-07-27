using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // async void is forced by the base signature — the try/catch is what keeps an unobserved
    // exception here from tearing down the app.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.LoadCountriesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[SettingsPage] OnAppearing failed: {ex}");
        }
    }
}
