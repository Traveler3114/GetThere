using GetThere.ViewModels;

namespace GetThere.Pages;

/// <summary>
/// Hosts the map page in a WebView.
/// <para>
/// There is deliberately nothing else here. The page is served by TransitInfoAPI and reads it
/// same-origin as an anonymous browser, so there is no token to hand over after navigation, and its
/// controls are its own — this class used to carry four <c>EvaluateJavaScriptAsync</c> bridges for
/// chrome that has since moved into the page.
/// </para>
/// </summary>
public partial class MapPage : ContentPage
{
    private readonly MapViewModel _viewModel;

    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Re-run on every appearance rather than once in the constructor: the URL carries the
        // current language, so returning to this tab after a language change reloads the page in it.
        _viewModel.LoadMapCommand.Execute(null);
    }
}
