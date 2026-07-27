using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class ShopPage : ContentPage
{
    private readonly ShopViewModel _viewModel;

    public ShopPage(ShopViewModel viewModel)
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
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ShopPage] OnAppearing failed: {ex}");
        }
    }
}
